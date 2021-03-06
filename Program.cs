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

        private static string notFinalBuf; // Last not final results
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

                RecognitionSpec rSpec = new RecognitionSpec()
                {
                    LanguageCode = "ru-RU",
                    ProfanityFilter = true,
                    Model = "general",
                    PartialResults = true, //возвращать только финальные результаты false
                    AudioEncoding = args.audioEncoding,
                    SampleRateHertz = args.sampleRate
                };

                 
                SpeechKitStreamClient speechKitClient =
                    new SpeechKitStreamClient(new Uri("https://stt.api.cloud.yandex.net:443"), args.folderId, args.iamToken, rSpec);
                // Subscribe for speech to text events comming from SpeechKit
                SpeechKitClient.SpeechToTextResponseReader.ChunkRecived += SpeechToTextResponseReader_ChunksRecived;

                outFile = File.AppendText(args.inputFilePath + ".speechkit.out");

                FileStreamReader filereader = new FileStreamReader(args.inputFilePath, logger);
                // Subscribe SpeechKitClient for next portion of audio data
                filereader.AudioBinaryRecived += speechKitClient.Listener_SpeechKitSend;
                filereader.ReadAudioFile().Wait();
               
                Log.Information("Shutting down SpeechKitStreamClient gRPC connections.");
                speechKitClient.Dispose();

                if (!string.IsNullOrEmpty(notFinalBuf))
                {
                    outFile.Write(notFinalBuf); //Write final results into file
                    outFile.Flush();
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Log.Error($"DeadlineExceeded: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }

            Log.Information("Execution compleated.");
        }
        static void HandleParseError(IEnumerable<Error> errs)
        {
            Log.Error($"Command line arguments parsing error.");
        }

        private static void SpeechToTextResponseReader_ChunksRecived(object sender, ChunkRecievedEventArgs e)
        {
            notFinalBuf = e.AsJson();
            Log.Information(notFinalBuf); // Log partial results

            if (e.SpeechToTextChunk.Final)
            {
                outFile.Write(notFinalBuf); //Write final results into file
                outFile.Flush();
                notFinalBuf = null; 
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

        [Option("sample-rate", Required = false, Default = 48000, HelpText = "The sampling frequency of the submitted audio (48000, 16000, 8000). Required if format is set to Linear16Pcm")]
        public int sampleRate { get; set; }

    }

}
