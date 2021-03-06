using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Text;
using Yandex.Cloud.Ai.Stt.V2;
using System.Xml;
using System.IO;

namespace Yandex.SpeechKit.ConsoleApp.SpeechKitClient
{
    /**
    * Speech recognition results event
    */
    public class SpeechToTextEventArgs : EventArgs
    {
        private String SpeechToTextResult;

        public string AsJsonString() { return AsJsonString(false); }
        public string AsJsonString(bool Indented)
        {
            if (Indented)
            {
                return FormatJsonText(SpeechToTextResult);
            }
            else
            {
              return SpeechToTextResult.ToString();
            }
        }

        internal SpeechToTextEventArgs(StreamingRecognitionResponse response)
        {
            SpeechToTextResult = JsonSerializer.Serialize(response.Chunks);
        }

        static string FormatJsonText(string jsonString)
        {
            using var doc = JsonDocument.Parse(
                jsonString,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true
                }
            );
            MemoryStream memoryStream = new MemoryStream();
            using (
                var utf8JsonWriter = new Utf8JsonWriter(
                    memoryStream,
                    new JsonWriterOptions
                    {
                        Indented = true
                    }
                )
            )
            {
                doc.WriteTo(utf8JsonWriter);
            }
            return new System.Text.UTF8Encoding()
                .GetString(memoryStream.ToArray());
        }

    }
}
