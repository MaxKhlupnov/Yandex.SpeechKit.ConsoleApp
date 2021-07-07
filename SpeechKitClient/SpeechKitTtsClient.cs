using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Speechkit.Tts.V3;


namespace Yandex.SpeechKit.ConsoleApp.SpeechKitClient
{
    class SpeechKitTtsClient : SpeechKitAbstractClient, IDisposable
    {

        private const int MAX_LINE_LENGTH = 5000;  //Ограничение на длину строки: 5000 символов. https://cloud.yandex.ru/docs/speechkit/tts/request

        private Synthesizer.SynthesizerClient synthesizerClient;

        public event EventHandler<AudioDataEventArgs> TextToSpeachResultsRecieved;

        private UtteranceSynthesisRequest ActiveRequest { get; set; }

        private String FolderId;

        public SpeechKitTtsClient(Uri address, string folderId, string IamToken, ILoggerFactory loggerFactory) : base(address, IamToken)
        {
            this.endpointAddress = address;
            this.FolderId = folderId;
                     
            synthesizerClient = new Synthesizer.SynthesizerClient(MakeChannel(loggerFactory));
        }


        public void SynthesizeTxtFile(string inputFilePath, string model)
        {
            var lines = File.ReadAllLines(inputFilePath);
            StringBuilder ttsBuffer = new StringBuilder(MAX_LINE_LENGTH); // 
            foreach (var line in lines)
            {
                if (ttsBuffer.Length + line.Length >= MAX_LINE_LENGTH)
                {
                    SynthesizeTxtBuffer(ttsBuffer.ToString(), model);
                    ttsBuffer = new StringBuilder(MAX_LINE_LENGTH);
                }
                ttsBuffer.AppendLine(line);
            }
            if (ttsBuffer.Length > 0)
            {
                SynthesizeTxtBuffer(ttsBuffer.ToString(), model);
            }
        }

        private  void SynthesizeTxtBuffer(string text, string model)
        {
            
            UtteranceSynthesisRequest request = MakeRequest(text, model);

            Metadata callHeaders = this.MakeMetadata();
            callHeaders.Add("x-folder-id", this.FolderId);

            var call = synthesizerClient.UtteranceSynthesis(request, headers: callHeaders,
                    deadline: DateTime.UtcNow.AddMinutes(5));

            IAsyncEnumerable<UtteranceSynthesisResponse> respEnumerable = call.ResponseStream.ReadAllAsync();
    
            var respEnum = respEnumerable.GetAsyncEnumerator();
            
            while (!respEnum.MoveNextAsync().GetAwaiter().IsCompleted)
            {
                Thread.Sleep(200);
            }

            byte[] audioData = respEnum.Current == null ? null : respEnum.Current.ToByteArray();

            /*   IAsyncEnumerator<UtteranceSynthesisResponse> respEnum = respEnumerable.GetAsyncEnumerator();
               if (respEnum.Current == null)
               {
                    respEnum.MoveNextAsync().GetAwaiter().OnCompleted(() =>
                          {
                              byte[] audioData = respEnum.Current == null ? null : respEnum.Current.ToByteArray();
                          }
                      );
               }
            */

            /* await foreach (UtteranceSynthesisResponse resp in resp.GetAsyncEnumerator().)
              {
                  byte[] audioData = resp.ToByteArray();
                  TextToSpeachResultsRecieved?.Invoke(this, AudioDataEventArgs.FromByateArray(audioData, audioData.Length));
              }*/
        }

        private UtteranceSynthesisRequest MakeRequest(string text, string model)
        {
            UtteranceSynthesisRequest utteranceRequest = new UtteranceSynthesisRequest
            {
                Model = model,
                Text = text,
                OutputAudioSpec = new AudioFormatOptions
                {
                    ContainerAudio = new ContainerAudio
                    {
                        ContainerAudioType = ContainerAudio.Types.ContainerAudioType.Wav
                    }
               }
            };

            return utteranceRequest;
        }

        public void Dispose()
        {
            
        }
    }
}
