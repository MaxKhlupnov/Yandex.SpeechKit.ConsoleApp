using Grpc.Core;
using Serilog;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yandex.Cloud.Ai.Stt.V2;

namespace Yandex.SpeechKit.ConsoleApp.SpeechKitClient
{
    public class SpeechToTextResponseReader     {
        /** active gRPC call fot SpeechKit service
        private AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> grpcCall;*/

        static internal event EventHandler<SpeechToTextEventArgs> ChunksRecived;

        /* private SpeechToTextResponseReader()
        {
            
        }

        Factory method to cerate an object and init thread
        internal static SpeechToTextResponseReader InitResponseReader(AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> grpcCall)
        {
            SpeechToTextResponseReader reader =  new SpeechToTextResponseReader(grpcCall);
            ThreadPool.QueueUserWorkItem(reader.ReadResponseStream));
            readingThread.Start();
            return reader;

        }*/

        internal static Task ReadResponseStream(AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> grpcCall)
        {
            return Task.Factory.StartNew(async () =>
    {
                ILogger log = Log.Logger;
                log.Information("Starting new ResponseStream reader");
                try
                {
                    /* await foreach (var response in this.grpcCall.ResponseStream.ReadAllAsync())
                     {
                         SpeechToTextEventArgs evt = new SpeechToTextEventArgs(response);
                         ChunksRecived?.Invoke(response, evt);
                     }*/

                    grpcCall.ResponseStream.ReadAllAsync();
                    while (await grpcCall.ResponseStream.MoveNext<StreamingRecognitionResponse>())
                    {
                        log.Information("Speech2Text response recieved");
                        SpeechToTextEventArgs evt = new SpeechToTextEventArgs(grpcCall.ResponseStream.Current);
                        ChunksRecived?.Invoke(grpcCall, evt);
                    }
                    /*
                     while (await this.grpcCall.ResponseStream.MoveNext<StreamingRecognitionResponse>())
                    {
                        log.Information("Speech 2 text response recieved");

                        foreach (SpeechRecognitionChunk chunk in this.grpcCall.ResponseStream.Current.Chunks)
                         {
                             foreach (SpeechRecognitionAlternative alt in chunk.Alternatives)
                             {
                                 log.Information($"alternative: {chunk.}");
                             }
                         }
                    }*/
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
                {
                    log.Warning("ResponseStream reader timeout");
                }
            });
        }
    }
}
