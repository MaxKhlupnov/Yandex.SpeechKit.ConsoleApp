using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Threading.Tasks;
using Yandex.Cloud.Ai.Stt.V2;
using Serilog;
using System.Threading;

namespace Yandex.SpeechKit.ConsoleApp.SpeechKitClient
{
    class SpeechKitStreamClient
    {

        public event EventHandler<SpeechToTextEventArgs> SpeechToTextResultsRecived;

        private RecognitionConfig rConf;
        private Serilog.ILogger log;
        private Uri endpointAddress;
        private String IamToken;
        private SttService.SttServiceClient speechKitRpctClient;
        private AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> _call;
        private Task _readTask;
        private Mutex callMutex = new Mutex(false,"callLock");
        //private SpeechToTextResponseReader _readTask;

        private  AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> ActiveCall()
        {
           
                if (this._call != null)
                {
                    try
                    {
                        Status status = this._call.GetStatus();
                        log.Information($"Call status: ${status.StatusCode} ${status.Detail}");
                       
                    
                        this._call = null;
                        if (this._readTask != null)
                        {
                            log.Information($"Call status: ${status.StatusCode} ${status.Detail}. Disposing.");
                            this._readTask.Dispose(); // Close read task
                        }
                        this._readTask = null;
                    
                        // call is finished
                    }
                    catch (Exception ex)
                    {
                        log.Information($"Call is in process - reuse. ${ex.Message}");
                        return this._call;
                    }
                }
            try
            {
                Log.Information($"Initialize gRPC call is finished");
                this._call = speechKitRpctClient.StreamingRecognize(headers: this.MakeMetadata());

                StreamingRecognitionRequest rR = new StreamingRecognitionRequest();
                rR.Config = this.rConf;

                this._call.RequestStream.WriteAsync(rR).Wait();

                // Start reading task for call
                if (this._readTask == null)
                {
                    this._readTask = SpeechToTextResponseReader.ReadResponseStream(this._call);
                    
                    /*  this._readTask = Task.Run(async () =>
                      {
                          this._call.ResponseStream.ReadAllAsync();
                          while (await this._call.ResponseStream.MoveNext<StreamingRecognitionResponse>())
                          {
                              foreach (SpeechRecognitionChunk chunk in this._call.ResponseStream.Current.Chunks)
                              {
                                  foreach (SpeechRecognitionAlternative alt in chunk.Alternatives)
                                  {
                                      log.Information($"alternative: {alt}");
                                  }
                              }
                          }
                      });*/
                }

                return this._call;
            }
            catch (RpcException ex) //when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Log.Warning($"gRPC request on create exception {ex.Message}  with status {ex.Status} code {ex.StatusCode}");
                // TODO : Reestablish connection
                return ActiveCall();
            }
        }

        private void _readTask_SpeechToTextResultsRecived(object sender, SpeechToTextEventArgs e)
        {
            SpeechToTextResultsRecived?.Invoke(sender,e);
        }

        public SpeechKitStreamClient(Uri address, string folderId, string IamToken) {
            
            this.log = Log.Logger;
            this.endpointAddress = address;
            this.IamToken = IamToken;

            RecognitionSpec rSpec = new RecognitionSpec()
            {
                LanguageCode = "ru-RU",
                ProfanityFilter = true,
                Model = "general",
                PartialResults = true,
                AudioEncoding = RecognitionSpec.Types.AudioEncoding.OggOpus
            };

            this.rConf = new RecognitionConfig()
            {
                FolderId = folderId,
                Specification = rSpec
            };

            ILoggerFactory _loggerFactory = Program.serviceProvider.GetService<ILoggerFactory>();

            SslCredentials sslCred = new Grpc.Core.SslCredentials();
            var chn = GrpcChannel.ForAddress(endpointAddress, new GrpcChannelOptions { LoggerFactory = _loggerFactory });
                        

            speechKitRpctClient = new SttService.SttServiceClient(chn);
            
        }

        

       internal void Listener_SpeechKitSend(object sender, AudioDataEventArgs e)
        {
                bool locked = callMutex.WaitOne(5 * 1000); // Всеравно тайм аут наступет через 5 сек. после прекращения записи на сервисе
                if (locked)
                {
                try
                {
                    AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> call = this.ActiveCall();
                    StreamingRecognitionRequest rR = new StreamingRecognitionRequest();
                    rR.AudioContent = Google.Protobuf.ByteString.CopyFrom(e.AudioData);
                    call.RequestStream.WriteAsync(rR).Wait();
                  //  call.RequestStream.CompleteAsync().Wait();
                  /* call.RequestStream.WriteAsync(rR).ContinueWith((task) =>
                   {
                       Task.Run(async () =>
                       {
                           call.ResponseStream.ReadAllAsync().ConfigureAwait(true);

                           while (await call.ResponseStream.MoveNext<StreamingRecognitionResponse>())
                           {
                               foreach (SpeechRecognitionChunk chunk in this._call.ResponseStream.Current.Chunks)
                               {
                                   foreach (SpeechRecognitionAlternative alt in chunk.Alternatives)
                                   {
                                       log.Information($"alternative: {alt}");
                                   }
                               }
                           }
                       });
                   }

                   );*/


                }
                catch (RpcException ex) //when (ex.StatusCode == StatusCode.DeadlineExceeded)
                {
                    Log.Error($"during data sent exception {ex.Message}  with status {ex.Status} code {ex.StatusCode}");
                    // TODO : Reestablish connection
                    throw ex;
                }
                finally
                {  if (locked)
                    {
                        callMutex.ReleaseMutex();
                    }
                    locked = false;
                }
                }
            //call.RequestStream.CompleteAsync().GetAwaiter().GetResult();

            //log.Information($"{rR.AudioContent.Length} bytes send to {endpointAddress}");
            //TODO: Переоткрывать соединение, если протух mutex

        }


        private Metadata MakeMetadata()
        {
            Metadata serviceMetadata = new Metadata();
            serviceMetadata.Add("authorization", $"Bearer {IamToken}");
            serviceMetadata.Add("x-data-logging-enabled", "true"); // 
            
            String requestId = Guid.NewGuid().ToString();

            serviceMetadata.Add("x-client-request-id",  requestId); /* уникальный идентификатор запроса. Рекомендуем использовать UUID. 
            Сообщите этот идентификатор технической поддержке, чтобы мы смогли найти конкретрный запрос в системе и помочь вам.*/
            log.Information($"Metadata configured for request: {requestId}");
            return serviceMetadata;
        }

    }
}
