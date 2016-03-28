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

        private static List<Location> LocationsDb = new List<Location>();

        private HttpClient _webClient;
        private HttpClient WebClient
        {
            get
            {
                if (_webClient == null)
                {
                    _webClient = new HttpClient();                   
                }
                return _webClient;
            }
        }

        public override async Task ReceiveAsync(Message message)
        {
            Console.WriteLine($"From: {message.From} \tContent: {message.Content}");
            await EnvelopeSender.SendNotificationAsync(message.ToConsumedNotification());

            try
            {

                var request = await BuildRequestFromMessageAsync(message);

                switch (request.CommandType)
                {
                    case CommandType.Help:
                        await ProcessHelpCommand(message, request);
                        break;
                    case CommandType.Route:
                        await ProcessSearchCommand(message, request);
                        break;
                    case CommandType.Location:
                        await ProcessLocationCommand(message, request);
                        break;
                    case CommandType.ListLocation:
                        await ProcessListLocationCommand(message, request);
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
            var text = message.Content.ToString().Trim().ToLower();
            var request = new Request();
            var @params = new Dictionary<string, string>();


            if (text.Contains("/ajuda") || text.Equals("1") || text.Equals("2"))
            {
                request.CommandType = CommandType.Help;

                if (text.Equals("1") || text.Equals("2"))
                {
                    @params["helpNumber"] = text;
                }

                request.Content = @params;
            }
            else if (text.Contains("/de") && text.Contains("/para"))
            {
                //TODO: Handle request without parameters or with missing parameter

                request.CommandType = CommandType.Route;

                var x = text.Trim().Split(new string[] { "/para" }, StringSplitOptions.None);
                var word1 = x[0].Replace("/de", "").Trim();
                var word2 = x[1].Trim();

                word1 = GetLocationByTag(message.From.ToIdentity(), word1) ?? word1;
                word2 = GetLocationByTag(message.From.ToIdentity(), word2) ?? word2;

                //TODO: Extract all magic strings to constants
                @params["origin"] = word1;
                @params["destination"] = word2;
                request.Content = @params;
            }
            else if (text.Contains("/cadastro"))
            {
                request.CommandType = CommandType.Location;

                var words = text.Trim().Replace("/cadastro ", "").Split(' ');
                var tag = words[0];

                var addressWords = words.Where(x => !x.Equals(tag)).ToArray();
                var address = GetStringByArray(addressWords);

                @params["tag"] = tag;
                @params["address"] = address;
                request.Content = @params;
            }
            else if (text.Contains("/pontos"))
            {
                request.CommandType = CommandType.ListLocation;
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

            await ExecuteSearchAsync(message, uri);
        }

        private async Task ProcessLocationCommand(Message message, Request request)
        {
            var location = new Location
            {
                Owner = message.From.ToIdentity(),
                Tag = request.Content["tag"].ToLower(),
                Address = request.Content["address"]
            };

            LocationsDb.Add(location);

            await EnvelopeSender.SendMessageAsync(@"Pronto! Ponto de referência cadastrado!", message.From);
        }

        private async Task ProcessListLocationCommand(Message message, Request request)
        {
            var builder = new System.Text.StringBuilder();

            foreach (var l in LocationsDb)
            {
                if (l.Owner.Equals(message.From.ToIdentity()))
                {
                    builder.AppendLine($"{l.Tag}: {l.Address}");
                }
            }
            
            if(builder.Length == 0)
            {
                builder.AppendLine("Você ainda não possui pontos de referencia cadastrados. Envia /ajuda para aprender como criar um novo!");
            }

            await EnvelopeSender.SendMessageAsync(builder.ToString(), message.From);
        }

        private async Task ProcessHelpCommand(Message message, Request request)
        {
            var messageHelp = "";

            if (request.Content.Keys.Contains("helpNumber"))
            {
                switch (request.Content["helpNumber"])
                {
                    //(o local de partida ou destino pode ser o endereço, o nome do estabelecimento ou algum de seus pontos de referência)
                    case "1":
                        messageHelp = @"Para pedir uma rota, me mande uma mensagem com seu ponto de partida utilizando o comando

/DE 'nome do local' /PARA 'nome do local'.

Exemplo: /DE Prado BH /PARA Pampulha BH MG";
                    break;
                    case "2":
                        messageHelp = @"Para cadastrar um ponto de referência, me mande o comando

/CADASTRO 'nome do ponto de referencia' 'endereço'. 

Exemplo: /CADASTRO trabalho Rua Paraguassu 83, Prado BH";
                        break;
                }
            }
            else
            {
                messageHelp = "Escolha a opção que deseja: \n 1- Como pedir rota \n 2- Como cadastrar pontos de referência";
            }

            var text = new PlainText
            {
                Text = messageHelp
            };

            await EnvelopeSender.SendMessageAsync(text, message.From);
        }

        private async Task<string> ComposeSearchUriAsync(IDictionary<string, string> settings)
        {
            Console.WriteLine($"Requested search by origin: {settings["origin"]} and destination: {settings["destination"]}!");

            return $"https://maps.googleapis.com/maps/api/directions/json?origin={settings["origin"]}&destination={settings["destination"]}&key={GoogleMapsAccessKey}&language=pt-BR";
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
                            if (gmapsObject.status == "NOT_FOUND")
                            {
                                await EnvelopeSender.SendMessageAsync(@"Desculpe, não entendi qual sua rota. Tente descrever melhor sua rota, tente incluir também sua cidade e país!", message.From);
                            }

                            dynamic route = gmapsObject.routes[0].legs[0];

                            Document resultRoute = ParseRoute(route);
                            await EnvelopeSender.SendMessageAsync(resultRoute, message.From);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Exception parsing response from Google Maps: {e}");
                            await EnvelopeSender.SendMessageAsync(@"Desculpe meus satélites estão passsando por alguns problemas no momento, tente mais tarde novamente!", message.From);
                            return null;
                        }
                    }
                }
            }

            return null;
        }

        private static Document ParseRoute(dynamic route)
        {
            string distance = route.distance.text;
            string duration = route.duration.text;
            var endAddress = route.end_address;
            var startAddress = route.start_address;
            var steps = route.steps;

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Rota encontrada:");
            builder.AppendLine("");
            builder.AppendLine($"Distancia: {distance}");
            builder.AppendLine($"Duração: {duration}");
            builder.AppendLine("");
            builder.AppendLine($"Principais vias: ");
            var i = 1;

            foreach (var s in steps)
            {
                var street = (string)s.html_instructions;
                street = street.Replace("<b>", "");
                street = street.Replace("</b>", "");
                street = street.Replace("<div style=\"font-size:0.9em\">", "\n");
                street = street.Replace("</div>", "");
                builder.AppendLine($"{i} - {street}");
                i++;
            }

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
            var resultItem = BuildMessage("", builder.ToString());
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

        private static string GetStringByArray(IEnumerable<string> words)
        {
            var builder = new System.Text.StringBuilder();

            foreach (var w in words)
            {
                var word = w.Replace("<b>", "");
                word = w.Replace("</b>", "");
                builder.Append(w + " ");
            }
            return builder.ToString();
        }

        private static string GetLocationByTag(Identity owner, string tag)
        {
            foreach(var l in LocationsDb)
            {
                if(l.Owner.Equals(owner) && l.Tag.Equals(tag.ToLower()))
                {
                    return l.Address;
                }
            }

            return null;
        }
    }
}
