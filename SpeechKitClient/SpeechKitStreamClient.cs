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
    class SpeechKitStreamClient : IDisposable
    {

        public event EventHandler<ChunkRecievedEventArgs> SpeechToTextResultsRecived;

        private RecognitionConfig rConf;
        private Serilog.ILogger log;
        private Uri endpointAddress;
        private String IamToken;
        private SttService.SttServiceClient speechKitRpctClient;
        private AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> _call;
        private Task _readTask;
        private Mutex callMutex = new Mutex(false,"callLock");
        //private SpeechToTextResponseReader _readTask;
        private int bytesSent = 0;
        private const int MAX_BYTES_SENT = 10 * 1024 * 1024; // check https://cloud.yandex.com/docs/speechkit/stt/streaming#session-restrictions for limitation details

        private  AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> ActiveCall()
        {
           
            if (this._call != null)
            {
                try
                {
                    Status status = this._call.GetStatus();
                    log.Information($"Call status: ${status.StatusCode} ${status.Detail}");

                    this._call.Dispose();
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
                this._call = speechKitRpctClient.StreamingRecognize(
                    headers: this.MakeMetadata(), 
                    deadline: DateTime.UtcNow.AddMinutes(5));  // check https://cloud.yandex.com/docs/speechkit/stt/streaming#session-restrictions for limitation details

                this.bytesSent = 0; // reset bytes counter
                StreamingRecognitionRequest rR = new StreamingRecognitionRequest();
                rR.Config = this.rConf;

                this._call.RequestStream.WriteAsync(rR).Wait();

                // Start reading task for call
                if (this._readTask == null)
                {
                    this._readTask = SpeechToTextResponseReader.ReadResponseStream(this._call);
                    
                }

                return this._call;
            }
            catch (RpcException ex) //when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Log.Warning($"gRPC request on create exception {ex.Message}  with status {ex.Status} code {ex.StatusCode}");
                // Maximum duration exeeded reestablish connection
                if (ex.StatusCode == StatusCode.DeadlineExceeded) //|| StatusCode=\"InvalidArgument\", Detail=\"audio should be less than 10MB\")"
                    return ActiveCall();
                else
                    throw ex;
            }
            catch (Exception ex) //when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Log.Error($"during data sent exception {ex.Message} ");               
                throw ex;
            }
}

        private void _readTask_SpeechToTextResultsRecived(object sender, ChunkRecievedEventArgs e)
        {
            SpeechToTextResultsRecived?.Invoke(sender,e);
        }

        public SpeechKitStreamClient(Uri address, string folderId, string IamToken, RecognitionSpec rSpec) {
            
            this.log = Log.Logger;
            this.endpointAddress = address;
            this.IamToken = IamToken;          

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
                    // recreate connection if we send more them 10 Mb                    
                    this.bytesSent += e.AudioData.Length;
                    if (this.bytesSent >= MAX_BYTES_SENT)
                        this._call = null;
                try
                {

                    WriteAudio(e.AudioData);

                }
                catch (Exception ex) //when (ex.StatusCode == StatusCode.DeadlineExceeded)
                {
                    Log.Error($"Error writing data: {ex.Message}\n Retrying... ");
                    this._call = null;
                    WriteAudio(e.AudioData);
                   
                }
                finally
                {
                    if (locked)
                    {
                        callMutex.ReleaseMutex();
                    }
                    locked = false;
                }
                }
        }


            private void WriteAudio(byte[] audioData)
            {
                try
                {
                    AsyncDuplexStreamingCall<StreamingRecognitionRequest, StreamingRecognitionResponse> call = this.ActiveCall();
                    StreamingRecognitionRequest rR = new StreamingRecognitionRequest();
                    rR.AudioContent = Google.Protobuf.ByteString.CopyFrom(audioData);
                    call.RequestStream.WriteAsync(rR).Wait();
                }
                catch (RpcException ex) //when (ex.StatusCode == StatusCode.DeadlineExceeded)
                {
                    Log.Error($"during data sent error: {ex.Message}  with status {ex.Status} code {ex.StatusCode}");                    
                    throw ex;
                }
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

        public void Dispose()
        {
            bool locked = callMutex.WaitOne(5 * 1000); // Всеравно тайм аут наступет через 5 сек. после прекращения записи на сервисе
            while (locked)
            {
                try
                {
                    if (this._call != null)
                    {
                        Status status = this._call.GetStatus(); // throw exception if not done
                        log.Information("Shutting down SpeechKit grpc connection.");
                        this._call.Dispose();
                        this._call = null;
                    }
                    
                    callMutex.ReleaseMutex();
                    locked = false;
                }
                catch (Exception ex)
                {
                    log.Information($"Waiting call for compleation. ${ex.Message}");
                    Thread.Sleep(1000);            
                }
            }
 
        }
    }
}
