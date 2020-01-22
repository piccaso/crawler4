using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Crawler3WebsocketClient;

namespace DockerCli {
    class Program {
        static void Main(string[] args) {

            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) => {
                if (cts.IsCancellationRequested) return;
                cts.Cancel(false);
                e.Cancel = true;
            };

            //ssh -L 127.0.0.1:2375:/var/run/docker.sock

            var logger = new LambdaLogger(Console.WriteLine);
            var jsonProc = new JsonProcessor(logger);
            var nodes = new HashSet<string>();
            jsonProc.OnEot += () => { Console.WriteLine("-- EOT! --"); };
            jsonProc.OnNode += (n) => {
                if(!nodes.Add(n.Url)) Console.WriteLine($"!! Duplicate URL:");
                Console.WriteLine(n.Title);
                Console.WriteLine(n.Url);
                Console.WriteLine($"{nodes.Count} Nodes");
            };
            jsonProc.OnEdges += (e) => { Console.WriteLine($"{e.Edges.Count} Edges"); };

            var baseUrl = "https://www.ichkoche.at/";
            var crawlerConfig = new CrawlerConfig {
                FollowInternalLinks = true,
                CheckExternalLinks = false,
                MaxRequestsPerCrawl = 500,
                TakeScreenShots = false,
                RequestQueue = {baseUrl},
                UrlFilter = $"{baseUrl}[.*]",
                MaxConcurrency = 6,
            };
            //var env = "CRAWLER_CONFIG='" + jsonProc.Serialize(crawlerConfig) + "'";
            //Console.WriteLine(env);
            var image = "quay.io/0xff/apify-crawler3:master";
            var dockerHost = "tcp://127.0.0.1:2375/";


            var si = new ProcessStartInfo {
                CreateNoWindow = true,
                FileName = "docker",
                ArgumentList = {
                    "run", "--rm", "-e", "CRAWLER_CONFIG", image
                },
                Environment = {
                    { "DOCKER_HOST", dockerHost},
                    { "CRAWLER_CONFIG", jsonProc.Serialize(crawlerConfig) }
                },
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            
            using (var p = new Process {StartInfo = si}) {
                try {
                    void DataReceived(object sender, DataReceivedEventArgs eventArgs) {
                        if(eventArgs.Data != null) jsonProc.ProcessMessage(eventArgs.Data);
                    }
                    p.OutputDataReceived += DataReceived;
                    p.ErrorDataReceived += DataReceived;
                    p.Start();
                    p.BeginErrorReadLine();
                    p.BeginOutputReadLine();
                    while (!cts.IsCancellationRequested) {
                        if(p.WaitForExit(1000)) break;
                    }
                    if (cts.IsCancellationRequested) {
                        Console.WriteLine("Ctrl+C");
                        p.StandardInput.WriteLine("\x3");
                        p.StandardInput.Close();
                        p.StandardOutput.Close();
                        p.StandardError.Close();
                        p.WaitForExit(20000);
                    }
                } finally {
                    p.Kill(true);
                }
                
                Console.WriteLine($"ExitCode={p.ExitCode}");
            }
        }
    }
}
