using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Flurl;
using Newtonsoft.Json.Linq;
using Noobot.Core.MessagingPipeline.Middleware;
using Noobot.Core.MessagingPipeline.Middleware.ValidHandles;
using Noobot.Core.MessagingPipeline.Request;
using Noobot.Core.MessagingPipeline.Response;
using Noobot.Core.Plugins.StandardPlugins;
using Noobot.Toolbox.Plugins;

public class HelperMiddleware : MiddlewareBase
{
    public class Joke
    {
        public string Content { get; set; }
    }

    public class SendUpdate
    {
        public string Pin { get; set; }
        public string Event { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
    }

    public class PinCode
    {
        public string Pin { get; set; }
        public string UserId { get; set; }
    }

    public class UpdateSubscription
    {
        public string Id { get; set; }
        public string Event { get; set; }
        public string WebhookUrl { get; set; }
    }

    private readonly StatsPlugin _statsPlugin;
    private readonly JsonStoragePlugin _jsonStoragePlugin;

    public HelperMiddleware(IMiddleware next, StatsPlugin statsPlugin, JsonStoragePlugin jsonStoragePlugin)
        : base(next)
    {
        _r = new Random();
        this._statsPlugin = statsPlugin;
        _jsonStoragePlugin = jsonStoragePlugin;
        this.HandlerMappings = new HandlerMapping[1]
        {
            new HandlerMapping()
            {
                ValidHandles = new[] {new AlwaysMatchHandle()},
                Description = "Supreme Ruler Roger Ver does not need to explain himself to you",
                EvaluatorFunc = DiscourseSearch
            }
        };
        jokes = jsonStoragePlugin.ReadFile<Joke>("jokes").ToList();
        PinCodes = jsonStoragePlugin.ReadFile<PinCode>("pincodes").ToList();
        UpdateSubscriptions = jsonStoragePlugin.ReadFile<UpdateSubscription>("subscriptions").ToList();
        if (!jokes.Any())
        {
            jokes.Add(new Joke()
            {
                Content = "bcash is the superior cryptocurrency because big blocks are cool, I guess"
            });
        }

        if (!PinCodes.Any())
        {
            PinCodes.Add(new PinCode()
            {
                Pin= "7dd7aa3a-a5e3-42d5-b651-1de152c75bcb",
                UserId= "U96S14QGY"
            });
        }
        
        _httpClient = new HttpClient();
    }

    public static List<Joke> jokes = new List<Joke>();
    public static List<PinCode> PinCodes = new List<PinCode>();
    public static List<UpdateSubscription> UpdateSubscriptions = new List<UpdateSubscription>();

    private Random _r;
    private HttpClient _httpClient;


    private IEnumerable<ResponseMessage> DiscourseSearch(IncomingMessage incomingMessage, IValidHandle validHandle)
    {
        foreach (var responseMessage1 in HandleNBitStack(incomingMessage)) yield return responseMessage1;

        foreach (var responseMessage in HandleJokes(incomingMessage)) yield return responseMessage;
        foreach (var responseMessage in HandleSendUpdate(incomingMessage)) yield return responseMessage;
        foreach (var responseMessage in HandleUpdateSubscription(incomingMessage)) yield return responseMessage;
        foreach (var responseMessage in HandlePinCodes(incomingMessage)) yield return responseMessage;
    }

    private bool IsPinValid(string pin, IncomingMessage incomingMessage)
    {
        return PinCodes.Any(code => code.Pin == pin && code.UserId == incomingMessage.UserId);
    }

    private IEnumerable<ResponseMessage> HandleSendUpdate(IncomingMessage incomingMessage)
    {
        if (incomingMessage.ChannelType != ResponseType.DirectMessage ||
            !incomingMessage.RawText.ToLower().StartsWith("sendupdate:")) yield break;
        var updateJson = incomingMessage.RawText.Substring("sendupdate:".Length);
        var update = JObject.Parse(updateJson).ToObject<SendUpdate>();

        var authenticated = IsPinValid(update.Pin, incomingMessage);
        if (!authenticated)
        {
            yield return incomingMessage.ReplyDirectlyToUser("You're not authorized to do this punk");
            yield break;
        }

        var subscriptions = UpdateSubscriptions.Where((subscription, i) =>
            string.IsNullOrEmpty(subscription.Event) || subscription.Event == update.Event);

        var channelUpdateMessage = $"*{update.Title}(_{update.Event}_)*{Environment.NewLine}{update.Message}{Environment.NewLine}@channel";

        yield return ResponseMessage.ChannelMessage("CDFGJ40Q5", channelUpdateMessage, (Attachment) null);

        var updates = new List<ResponseMessage>();
        var tasks = subscriptions.Select((subscription, i) =>
        {
            var template = subscription.WebhookUrl
                .Replace("{event}", "{0}")
                .Replace("{title}", "{1}")
                .Replace("{message}", "{2}");

            var url = String.Format(template, update.Event, update.Title, update.Message);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            updates.Add(incomingMessage.ReplyDirectlyToUser($"Sending update to {url}"));
            return _httpClient.SendAsync(request);
        });
        foreach (var responseMessage in updates)
        {
            yield return responseMessage;
        }

        updates = Task.WhenAll(tasks).Result.Select(message =>
        {
            return incomingMessage.ReplyDirectlyToUser(
                $"{(message.IsSuccessStatusCode ? "Success" : "Failure")}: {message.RequestMessage.RequestUri}");
        }).ToList();
        foreach (var responseMessage in updates)
        {
            yield return responseMessage;
        }
    }

    private IEnumerable<ResponseMessage> HandleUpdateSubscription(IncomingMessage incomingMessage)
    {
        if (
            incomingMessage.RawText.ToLower().StartsWith("updatesubscribe:"))
        {
            var updateJson = incomingMessage.RawText.Substring("updatesubscribe:".Length);
            var update = JObject.Parse(updateJson).ToObject<UpdateSubscription>();
            update.Id = Guid.NewGuid().ToString();
            update.WebhookUrl = update.WebhookUrl.Trim('<', '>');
            UpdateSubscriptions.Add(update);
            _jsonStoragePlugin.SaveFile("subscriptions", UpdateSubscriptions.ToArray());
            yield return incomingMessage.ReplyDirectlyToUser(
                $"Subscription added with id `{update.Id}` {Environment.NewLine}" +
                $"You may unsubscribe by sending `updateunsubscribe:{update.Id}` to me in DM");
        }

        if (
            incomingMessage.RawText.ToLower().StartsWith("updateunsubscribe:"))
        {
            var id = incomingMessage.RawText.Substring("updateunsubscribe:".Length).Trim();
            var index = UpdateSubscriptions.FindIndex(subscription => subscription.Id == id);
            if (index >= 0)
            {
                UpdateSubscriptions.RemoveAt(index);
                _jsonStoragePlugin.SaveFile("subscriptions", UpdateSubscriptions.ToArray());
                yield return incomingMessage.ReplyDirectlyToUser($"Subscription removed");
            }
            else
            {
                yield return incomingMessage.ReplyDirectlyToUser($"Subscription was not found,");
            }
        }
    }

    private IEnumerable<ResponseMessage> HandlePinCodes(IncomingMessage incomingMessage)
    {
        if (
            incomingMessage.RawText.ToLower().StartsWith("addpin:"))
        {
            var updateJson = incomingMessage.RawText.Substring("addpin:".Length);
            var update = JObject.Parse(updateJson).ToObject<AddPin>();

            var authenticated = IsPinValid(update.Pin, incomingMessage);
            if (!authenticated)
            {
                yield return incomingMessage.ReplyDirectlyToUser("You're not authorized to do this punk");
                yield break;
            }

            if (PinCodes.Any(code => code.UserId == update.UserId))
            {
                yield return incomingMessage.ReplyDirectlyToUser("You're not authorized to do this punk");
                yield break;
            }

            var newPinCode = Guid.NewGuid().ToString();
            PinCodes.Add(new PinCode()
            {
                Pin = newPinCode,
                UserId = update.UserId
            });

            _jsonStoragePlugin.SaveFile("pincodes", PinCodes.ToArray());

            yield return ResponseMessage.DirectUserMessage(update.UserId,
                $"You have been granted a pin code: `{newPinCode}` " +
                "You may change it by sending " +
                "`changepin:{\"Pin\": \"" + newPinCode + "\"}`");
            yield return incomingMessage.ReplyDirectlyToUser($"Pin generated for user  {update.UserId}.");
        }

        if (
            incomingMessage.RawText.ToLower().StartsWith("changepin:"))
        {
            var updateJson = incomingMessage.RawText.Substring("changepin:".Length);
            var update = JObject.Parse(updateJson).ToObject<ChangePin>();

            var authenticated = IsPinValid(update.Pin, incomingMessage);
            if (!authenticated)
            {
                yield return incomingMessage.ReplyDirectlyToUser("You're not authorized to do this punk");
                yield break;
            }

            var newPinCode = Guid.NewGuid().ToString();
            var i = PinCodes.FindIndex(code => code.UserId == incomingMessage.UserId);
            PinCodes[i].Pin = newPinCode;

            _jsonStoragePlugin.SaveFile("pincodes", PinCodes.ToArray());

            yield return incomingMessage.ReplyDirectlyToUser($"You have been granted a pin code: `{newPinCode}` " +
                                                             "You may change it by sending " +
                                                             "`changepin:{\"Pin\": \"" + newPinCode + "\"}`");
        }
    }

    public class AddPin
    {
        public string Pin { get; set; }
        public string UserId { get; set; }
    }

    public class ChangePin
    {
        public string Pin { get; set; }
        public string NewPin { get; set; }
    }


    private IEnumerable<ResponseMessage> HandleNBitStack(IncomingMessage incomingMessage)
    {
        var genericAnswerKeywords = new List<string>() {"question", "i have a question", "i need help"};
        if (genericAnswerKeywords.Contains(incomingMessage.RawText.ToLower()))
        {
            yield return incomingMessage.IndicateTypingOnChannel();
            _statsPlugin.IncrementState("Helper:BlindlyHelped");
            yield return incomingMessage.ReplyToChannel(
                $"Perhaps you should look at https://nbitstack.com/c/btcpayserver and see if there is already an answer for you there, @{incomingMessage.Username}");
        }

        var discourseSearchKeywords = new List<string>() {"how can i", "i need help with"};
        if (discourseSearchKeywords.Any(s => incomingMessage.RawText.ToLower().StartsWith(s.ToLower())))
        {
            var discourseUrl = "https://nbitstack.com";
            var searchText = incomingMessage.RawText;
            discourseSearchKeywords.ForEach(s => { searchText = searchText.Replace(s, ""); });
            yield return incomingMessage.IndicateTypingOnChannel();
            var message = $"Maybe this can help: {discourseUrl}/search?q={Url.Encode(searchText.Trim())}";

            _statsPlugin.IncrementState("RedirectedToDiscourseSearch:Count");
            yield return incomingMessage.ReplyToChannel(message);
        }
    }

    private IEnumerable<ResponseMessage> HandleJokes(IncomingMessage incomingMessage)
    {
        var bcashKeywords = new List<string>() {"bcash", "bitcoin cash", "tell me a joke"};
        if (bcashKeywords.Contains(incomingMessage.RawText.ToLower()))
        {
            yield return incomingMessage.IndicateTypingOnChannel();

            _statsPlugin.IncrementState("Jokes:Told");

            yield return incomingMessage.ReplyToChannel(jokes[_r.Next(0, jokes.Count)].Content);
        }

        if (incomingMessage.RawText.ToLower().StartsWith("addjoke:"))
        {
            var joke = incomingMessage.RawText.Substring("addjoke:".Length);
            jokes.Add(new Joke()
            {
                Content = joke
            });
            _jsonStoragePlugin.SaveFile("jokes", jokes.ToArray());
            yield return incomingMessage.ReplyDirectlyToUser("joke added!");
        }

        if (incomingMessage.RawText.ToLower().StartsWith("removejoke:"))
        {
            var joke = incomingMessage.RawText.Substring("removejoke:".Length);
            if (jokes.Any(joke1 => joke == joke1.Content))
            {
                var jokeIndex = jokes.FindIndex(joke1 => joke == joke1.Content);

                jokes.RemoveAt(jokeIndex);
                _jsonStoragePlugin.SaveFile("jokes", jokes.ToArray());
                yield return incomingMessage.ReplyDirectlyToUser("joke removed!");
            }
        }
    }
}