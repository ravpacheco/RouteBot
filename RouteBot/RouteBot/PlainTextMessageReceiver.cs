using Lime.Messaging.Contents;
using Lime.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RouteBot.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Takenet.MessagingHub.Client;
using Takenet.MessagingHub.Client.Receivers;

namespace RouteBot
{
    public class PlainTextMessageReceiver : MessageReceiverBase
    {
        private static readonly MediaType ResponseMediaType = new MediaType("application", "vnd.omni.text", "json");
        private static readonly string GoogleMapsAccessKey = "AIzaSyCenpBUF5QaHSAaGcrjbeq3oxf8uK_uR7Y";

        private HttpClient _webClient;
        private HttpClient WebClient
        {
            get
            {
                if (_webClient == null)
                {
                    _webClient = new HttpClient();
                    //_webClient.DefaultRequestHeaders.Add("app-token", Settings["buscapeAppToken"].ToString());
                }
                return _webClient;
            }
        }

        public override async Task ReceiveAsync(Message message)
        {
            Console.WriteLine($"From: {message.From} \tContent: {message.Content}");
            await EnvelopeSender.SendMessageAsync("Pong!", message.From);
            await EnvelopeSender.SendNotificationAsync(message.ToConsumedNotification());

            try
            {

                var request = await BuildRequestFromMessageAsync(message);

                switch (request.CommandType)
                {
                    case CommandType.Help:
                        await ProcessHelpCommand(request);
                        break;
                    case CommandType.Route:
                        await ProcessSearchCommand(message, request);
                        break;
                    case CommandType.Error:
                        //Handle this error
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error on handle request for message {message}!");
                await EnvelopeSender.SendMessageAsync("Ops, não consegui localizar meus mapas, tente mais tarde novamente!", message.From);
            }

        }

        private async Task<Request> BuildRequestFromMessageAsync(Message message)
        {
            var text = message.Content.ToString();
            var request = new Request();
            var @params = new Dictionary<string, string>();


            if (text.Contains("/ajuda"))
            {
                request.CommandType = CommandType.Help;
                request.Content = @params;
            }
            else if (text.Contains("/de") && text.Contains("/para"))
            {
                //TODO: Handle request without parameters or with missing parameter

                request.CommandType = CommandType.Route;
                
                var x = text.Trim().Split(new string[] { "/para" }, StringSplitOptions.None);
                var word1 = x[0].Replace("/de", "");
                var word2 = x[1];

                //TODO: Extract all magic strings to constants
                @params["origin"] = word1;
                @params["destination"] = word2;
                request.Content = @params;
            }
            else
            {
                request.CommandType = CommandType.Error;
                request.Content = @params;
            }

            return request;
        }

        private async Task ProcessSearchCommand(Message message, Request request)
        {
            var uri = await ComposeSearchUriAsync(request.Content);

            var result = await ExecuteSearchAsync(message, uri);
        }

        private Task ProcessHelpCommand(Request request)
        {
            throw new NotImplementedException();
        }

        private async Task<string> ComposeSearchUriAsync(IDictionary<string, string> settings)
        {
            Console.WriteLine($"Requested search by origin: {settings["origin"]} and destination: {settings["destination"]}!");

            return $"https://maps.googleapis.com/maps/api/directions/json?origin={settings["origin"]}&destination={settings["destination"]}&key={GoogleMapsAccessKey}";
        }


        private async Task<object> ExecuteSearchAsync(Message message, string uri)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var response = await WebClient.SendAsync(request, cancellationTokenSource.Token);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        await EnvelopeSender.SendMessageAsync(@"Ops, não consegui localizar meus mapas, tente mais tarde novamente!", message.From);
                    }
                    else
                    {
                        var resultJson = await response.Content.ReadAsStringAsync();
                        dynamic gmapsObject = JsonConvert.DeserializeObject(resultJson);
                        try
                        {
                            if(gmapsObject.status == "NOT_FOUND")
                            {
                                await EnvelopeSender.SendMessageAsync(@"Desculpe, não entendi qual sua rota. Tente descrever melhor sua rota, tente incluir também sua cidade e país!", message.From);
                            }

                            dynamic route = gmapsObject.routes[0].legs[0];

                            //var resultRoute = ParseRoute(route);
                            Document resultRoute = ParseRoute(route);
                            await EnvelopeSender.SendMessageAsync(resultRoute, message.From);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Exception parsing response from Buscapé: {e}");
                            await EnvelopeSender.SendMessageAsync("Nenhum resultado encontrado", message.From);
                            return null;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
                        await EnvelopeSender.SendMessageAsync($"Envie: ; ", message.From);
                    }
                }
            }

            return null;
        }

        private static Document ParseRoute(dynamic route)
        {
            string distance = route.distance.text;
            string duration = route.text;
            var endAddress = route.end_address;
            var startAddress = route.start_address;
            var steps = route.steps;

            var message = "";

            var builder = new System.Text.StringBuilder();
            builder.Append(message);

            foreach (var s in steps)
            {
                builder.Append(s.html_instructions);
            }
            message = builder.ToString();

            //var obj = route.Properties().First();
            //var name = obj.Value["productname"]?.Value<string>() ??
            //           obj.Value["productshortname"]?.Value<string>() ?? "Produto Desconhecido!";
            //var pricemin = obj.Value["pricemin"]?.Value<string>();
            //var pricemax = obj.Value["pricemax"]?.Value<string>();
            //var text = name;
            //if (pricemin != null && pricemax != null)
            //    text += $"\nDe R$ {pricemin} a R$ {pricemax}.";
            //var thumbnail =
            //    obj.Value["thumbnail"]["formats"].Single(
            //        f => f["formats"]["width"].Value<int>() == 100)["formats"]["url"]
            //        .Value<string>();
            //var link =
            //    obj.Value["links"].Single(
            //        l => l["link"]["type"].Value<string>() == "product")["link"]["url"]
            //        .Value<string>();
            var resultItem = BuildMessage("", message);
            return resultItem;
        }

        private static Document BuildMessage(string imageUri, string text)
        {
            Document document = null;

            if (string.IsNullOrEmpty(imageUri))
            {
                document = new PlainText
                {
                    Text = $"{text}"
                };
            }

            //document = new JsonDocument(ResponseMediaType)
            //{
            //    {
            //        nameof(text), link != null ? $"{text}\n{link}" : text
            //    }
            //};
            //
            //var attachments = new List<IDictionary<string, object>>();
            //
            //var attachment = new Dictionary<string, object>
            //{
            //    {"mimeType", "image/jpeg"},
            //    {"mediaType", "image"},
            //    {"size", 100},
            //    {"remoteUri", imageUri},
            //    {"thumbnailUri", imageUri}
            //};
            //attachments.Add(attachment);
            //
            //document.Add(nameof(attachments), attachments);
            //
            return document;
        }

    }
}
