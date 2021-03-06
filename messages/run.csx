using System;
using System.Threading.Tasks;

using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Luis.Models;
using Microsoft.Bot.Builder.Luis;

using System.Collections.Generic;
using Microsoft.Bot.Connector;
using Microsoft.Bot.Builder.Dialogs;
using System.Configuration;
using System.Text;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Search.Query;

// For more information about this template visit http://aka.ms/azurebots-csharp-luis
[Serializable]
[LuisModel("3a84de09-a4af-4517-af9a-ce7614491c66", "062f684de7bf4905b764e3bb01598288")]
public class BasicLuisDialog : LuisDialog<object>
{
    const string SPAccessTokenKey = "SPAccessToken";
        const string SPSite = "https://nagarro.sharepoint.com/sites/teams/development";
        private static readonly Dictionary<string, string> PropertyMappings = new Dictionary<string, string>
        {
            { "TypeOfDocument", "botDocType" },
            { "Software", "BotTopic" }
        };

        [Serializable]
        public class PartialMessage
        {
            public string Text { set; get; }
        }
        private PartialMessage message;
    [LuisIntent("None")]
    public async Task NoneIntent(IDialogContext context, LuisResult result)
    {
        await context.PostAsync($"You have reached the none intent. You said: {result.Query}"); //
        context.Wait(MessageReceived);
    }

    // Go to https://luis.ai and create a new intent, then train/publish your luis app.
    // Finally replace "MyIntent" with the name of your newly created intent in the following handler
     [LuisIntent("FindDocumentation")]
        public async Task Search(IDialogContext context, IAwaitable<IMessageActivity> activity, LuisResult result)
        {
            var reply = context.MakeMessage();
            try
            {
                reply.AttachmentLayout = AttachmentLayoutTypes.Carousel;
                reply.Attachments = new List<Microsoft.Bot.Connector.Attachment>();
                StringBuilder query = new StringBuilder();
                bool QueryTransformed = false;
                if (result.Entities.Count > 0)
                {
                    QueryTransformed = true;
                    foreach (var entity in result.Entities)
                    {
                        if (PropertyMappings.ContainsKey(entity.Type))
                        {
                            query.AppendFormat("{0}:'{1}' ", PropertyMappings[entity.Type], entity.Entity);
                        }
                    }
                }
                else
                {
                    //should replace all special chars
                    query.Append(this.message.Text.Replace("?", ""));
                }

                using (ClientContext ctx = new ClientContext(SPSite))
                {
                    ctx.AuthenticationMode = ClientAuthenticationMode.Anonymous;
                    ctx.ExecutingWebRequest +=
                        delegate (object oSender, WebRequestEventArgs webRequestEventArgs)
                        {
                            webRequestEventArgs.WebRequestExecutor.RequestHeaders["Authorization"] =
                                "Bearer " + context.UserData.Get<string>("SPAccessToken");
                        };
                    KeywordQuery kq = new KeywordQuery(ctx);
                    kq.QueryText = string.Concat(query.ToString(), " IsDocument:1");
                    kq.RowLimit = 5;
                    SearchExecutor se = new SearchExecutor(ctx);
                    ClientResult<ResultTableCollection> results = se.ExecuteQuery(kq);
                    ctx.ExecuteQuery();

                    if (results.Value != null && results.Value.Count > 0 && results.Value[0].RowCount > 0)
                    {
                        reply.Text += (QueryTransformed == true) ? "I found some interesting reading for you!" : "I found some potential interesting reading for you!";
                        BuildReply(results, reply);
                    }
                    else
                    {
                        if (QueryTransformed)
                        {
                            //fallback with the original message
                            kq.QueryText = string.Concat(this.message.Text.Replace("?", ""), " IsDocument:1");
                            kq.RowLimit = 3;
                            se = new SearchExecutor(ctx);
                            results = se.ExecuteQuery(kq);
                            ctx.ExecuteQuery();
                            if (results.Value != null && results.Value.Count > 0 && results.Value[0].RowCount > 0)
                            {
                                reply.Text += "I found some potential interesting reading for you!";
                                BuildReply(results, reply);
                            }
                            else
                                reply.Text += "I could not find any interesting document!";
                        }
                        else
                            reply.Text += "I could not find any interesting document!";

                    }

                }

            }
            catch (Exception ex)
            {
                reply.Text = ex.Message;
            }
            await context.PostAsync(reply);
            context.Wait(MessageReceived);
        }
        void BuildReply(ClientResult<ResultTableCollection> results, IMessageActivity reply)
        {
            foreach (var row in results.Value[0].ResultRows)
            {
                List<CardAction> cardButtons = new List<CardAction>();
                List<CardImage> cardImages = new List<CardImage>();
                string ct = string.Empty;
                string icon = string.Empty;
                switch (row["FileExtension"].ToString())
                {
                    case "docx":
                        ct = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                        icon = "https://cdn2.iconfinder.com/data/icons/metro-ui-icon-set/128/Word_15.png";
                        break;
                    case "xlsx":
                        ct = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                        icon = "https://cdn2.iconfinder.com/data/icons/metro-ui-icon-set/128/Excel_15.png";
                        break;
                    case "pptx":
                        ct = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                        icon = "https://cdn2.iconfinder.com/data/icons/metro-ui-icon-set/128/PowerPoint_15.png";
                        break;
                    case "pdf":
                        ct = "application/pdf";
                        icon = "https://cdn4.iconfinder.com/data/icons/CS5/256/ACP_PDF%202_file_document.png";
                        break;

                }
                cardButtons.Add(new CardAction
                {
                    Title = "Open",
                    Value = (row["ServerRedirectedURL"] != null) ? row["ServerRedirectedURL"].ToString() : row["Path"].ToString(),
                    Type = ActionTypes.OpenUrl
                });
                cardImages.Add(new CardImage(url: icon));
                ThumbnailCard tc = new ThumbnailCard();
                tc.Title = (row["Title"] != null) ? row["Title"].ToString() : "Untitled";
                tc.Text = (row["Description"] != null) ? row["Description"].ToString() : string.Empty;
                tc.Images = cardImages;
                tc.Buttons = cardButtons;
                reply.Attachments.Add(tc.ToAttachment());
            }
        }

}