using Lime.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
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
        public override async Task ReceiveAsync(Message message)
        {
            Console.WriteLine($"From: {message.From} \tContent: {message.Content}");
            await EnvelopeSender.SendMessageAsync("Pong!", message.From);
            await EnvelopeSender.SendNotificationAsync(message.ToConsumedNotification());
        }

        private async Task ExecuteSearchAsync(Message message, string uri)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    var client = new WebClient();
                    var buscapeResponse = await client.DownloadStringAsync(new Uri(uri));
                    if (buscapeResponse.StatusCode != HttpStatusCode.OK)
                    {
                        await EnvelopeSender.SendMessageAsync(@"Não foi possível obter uma resposta do Buscapé!", message.From);
                    }
                    else
                    {
                        var resultJson = await buscapeResponse.Content.ReadAsStringAsync();
                        dynamic responseMessage = JsonConvert.DeserializeObject(resultJson);
                        try
                        {
                            foreach (JObject product in responseMessage.product)
                            {
                                try
                                {
                                    //var resultItem = ParseProduct(product);
                                    //await EnvelopeSender.SendMessageAsync(resultItem, message.From);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"Exception parsing product: {e}");
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Exception parsing response from Buscapé: {e}");
                            await EnvelopeSender.SendMessageAsync("Nenhum resultado encontrado", message.From);
                            return;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
                        await EnvelopeSender.SendMessageAsync($"Envie: ; ", message.From);
                    }
                }
            }
        }
    }
}
