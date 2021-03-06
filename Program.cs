using CommandLine;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Yandex.Cloud.Ai.Stt.V2;
using Yandex.SpeechKit.ConsoleApp.SpeechKitClient;

namespace Yandex.SpeechKit.ConsoleApp
{

    class Program
    {
        public static IServiceProvider serviceProvider = ConfigureServices(new ServiceCollection());
        private static StreamWriter outFile;

        static void Main(string[] args)
        {

            CommandLine.Parser.Default.ParseArguments<Options>(args)
                .WithParsed(RunOptions)
                .WithNotParsed(HandleParseError);

        }

        static void RunOptions(Options args)
        {
            ILoggerFactory _loggerFactory = Program.serviceProvider.GetService<ILoggerFactory>();
            _loggerFactory.AddSerilog();
            var logger = Log.Logger;

            try
            {

                SpeechKitStreamClient speechKitClient =
                    new SpeechKitStreamClient(new Uri("https://stt.api.cloud.yandex.net:443"), args.folderId, args.iamToken);
                SpeechKitClient.SpeechToTextResponseReader.ChunkRecived += SpeechToTextResponseReader_ChunksRecived;

                outFile = File.AppendText(args.inputFilePath + ".speechkit.out");

                FileStreamReader filereader = new FileStreamReader(args.inputFilePath, logger);
                filereader.AudioBinaryRecived += speechKitClient.Listener_SpeechKitSend;
                filereader.ReadAudioFile();

            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Log.Error($"DeadlineExceeded: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }

            Console.WriteLine("Press any key to cancel");
            Console.ReadKey();


        }
        static void HandleParseError(IEnumerable<Error> errs)
        {
            Log.Error($"Command line arguments parsing error.");
        }

        private static void SpeechToTextResponseReader_ChunksRecived(object sender, ChunkRecievedEventArgs e)
        {
            if (e.SpeechToTextChunk.Final)
            {
                string chunkJson = e.AsJson();
                Log.Information(chunkJson);
                  outFile.Write(chunkJson);
            }
        }


        private static IServiceProvider ConfigureServices(IServiceCollection services)
        {
            var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json");

            var config = builder.Build();

            Log.Logger = new LoggerConfiguration()
                           .ReadFrom.Configuration(config)
                           .Enrich.FromLogContext()
                        .MinimumLevel.Debug()
                           .CreateLogger();

            services.AddSingleton<ILoggerFactory, LoggerFactory>();

            services.AddLogging();

            var serviceProvider = services.BuildServiceProvider();
            return serviceProvider;

        }
    }

    public class Options 
    {
        [Option("iam-token", Required = true, HelpText = "Specify the received IAM token when accessing Yandex.Cloud SpeechKit via the API.")]
        public string iamToken { get; set; }

       [Option("folder-id", Required = true, HelpText = "ID of the folder that you have access to.")]
        public String folderId { get; set; }

        [Option("in-file", Required = true, HelpText = "Path of the audio file for recognition.")]
        public string inputFilePath { get; set; }

        [Option("audio-encoding", Required = true, HelpText = "The format of the submitted audio. Acceptable values: Linear16Pcm, OggOpu.")]
        public RecognitionSpec.Types.AudioEncoding audioEncoding { get; set; }

    }

}
