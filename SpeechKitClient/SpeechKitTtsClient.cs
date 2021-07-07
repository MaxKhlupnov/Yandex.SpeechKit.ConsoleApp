using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Speechkit.Tts.V3;
using System.Threading.Tasks;

namespace Yandex.SpeechKit.ConsoleApp.SpeechKitClient
{
    class SpeechKitTtsClient : SpeechKitAbstractClient, IDisposable
    {

        private const int MAX_LINE_LENGTH = 160;  //Ограничение на длину строки: 160 символов. https://cloud.yandex.ru/docs/speechkit/tts/request

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
            
            foreach (String line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    SynthesizeTxtLine(line, model);
                }

                
            }
        }

        private void SynthesizeTxtLine(string text, string model)
        {
            StringBuilder ttsBuffer = new StringBuilder(MAX_LINE_LENGTH);

            string[] parts = Regex.Split(text, @"(?<=[.,;])");

            foreach (String line in parts)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (ttsBuffer.Length + line.Length >= MAX_LINE_LENGTH)
                    {
                        SynthesizeTxtBuffer(ttsBuffer.ToString(), model);
                        ttsBuffer = new StringBuilder(MAX_LINE_LENGTH);
                    }

                    ttsBuffer.AppendLine(line);
                }
            }

            if (ttsBuffer.Length > 0)
            {
                SynthesizeTxtBuffer(ttsBuffer.ToString(), model);
            }
        }


        private async void SynthesizeTxtBuffer(string text, string model)
        {
            try
            {
                UtteranceSynthesisRequest request = MakeRequest(text, model);

                Metadata callHeaders = this.MakeMetadata();
                callHeaders.Add("x-folder-id", this.FolderId);

                using (var call = synthesizerClient.UtteranceSynthesis(request, headers: callHeaders,
                        deadline: DateTime.UtcNow.AddMinutes(5)))
                {
                    CancellationToken ct = new CancellationToken(false);
                    IAsyncEnumerable<UtteranceSynthesisResponse> respEnumerable =  call.ResponseStream.ReadAllAsync(ct);

                    IAsyncEnumerator<UtteranceSynthesisResponse> respEnum = respEnumerable.GetAsyncEnumerator();

                  /*  while (!respEnum.MoveNextAsync().GetAwaiter().IsCompleted)
                    {
                        Thread.Sleep(200);
                    }

                    byte[] data = respEnum.Current.AudioChunk.Data.ToByteArray();
                    TextToSpeachResultsRecieved?.Invoke(this, AudioDataEventArgs.FromByateArray(data,
                       data.Length));*/

                    await respEnum.DisposeAsync();
                    call.Dispose();

                }
                
            }catch(Exception ex)
            {
                log.Error(ex.Message);
            }
        }

        private static UtteranceSynthesisRequest MakeRequest(string text, string model)
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
