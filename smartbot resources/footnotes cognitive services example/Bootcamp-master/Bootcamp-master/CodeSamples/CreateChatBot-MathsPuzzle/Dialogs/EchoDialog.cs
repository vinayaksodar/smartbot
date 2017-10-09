using System;
using System.Threading.Tasks;

using Microsoft.Bot.Connector;
using Microsoft.Bot.Builder.Dialogs;
using System.Net.Http;
using System.Collections.Generic;

namespace MathsPuzzle
{
    [Serializable]
    public class EchoDialog : IDialog<object>
    {
        public async Task StartAsync(IDialogContext context)
        {
            context.Wait(MessageReceivedAsync);
        }
        public async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> request)
        {
            var message = await request;
            string userName;

            if(!context.UserData.TryGetValue(ContextConstants.UserName, out userName))
            {
                await context.PostAsync("Heyyyy!");
                await context.PostAsync("Welcome to the Maths bot. I am going to ask you some questions and lets see how you score.");
                PromptDialog.Text(context, this.ResumeAfterUserName, "Before getting started, please tell me your name?");
                return;
            }
            int questionsAsked;
            if (!context.ConversationData.TryGetValue(nameof(ContextConstants.QuestionsAsked), out questionsAsked))
            {
                await context.PostAsync("Some problem in our service. Please try after sometime");
                context.UserData.Clear();
                context.ConversationData.Clear();
            }
            else
            {
                if (questionsAsked < 4)
                {
                    await UpdateScore(context, request, questionsAsked);
                    await AskQuestion(context, questionsAsked + 1);
                    context.ConversationData.SetValue(nameof(ContextConstants.QuestionsAsked), questionsAsked + 1);
                }
                else
                {
                    await UpdateScore(context, request, questionsAsked);
                    int score;
                    if (!context.ConversationData.TryGetValue(nameof(ContextConstants.Score), out score))
                    {
                        score = 0;
                    }
                    await context.PostAsync($"Congratulations {userName}. You scored {score} points!!");
                    context.UserData.Clear();
                    context.ConversationData.Clear();
                }
            }

            context.Done<object>(new object());
        }

        private async Task UpdateScore(IDialogContext context, IAwaitable<IMessageActivity> request, int questionNumber)
        {
            string response = (await request).Text;
            int score;
            if (!context.ConversationData.TryGetValue(nameof(ContextConstants.Score), out score))
            {
                score = 0;
            }
            switch(questionNumber)
            {
                case 1:
                    score += (response == "27" ? 10 : 0);
                    break;
                case 2:
                    score += (response == "287" ? 10 : 0);
                    break;
                case 3:
                    score += (response == "961" ? 10 : 0);
                    break;
                case 4:
                    score += (response == "164" ? 10 : 0);
                    break;
            }
            context.ConversationData.SetValue(nameof(ContextConstants.Score), score);
        }

        private async Task AskQuestion(IDialogContext context, int questionNumber)
        {
            var message = context.MakeMessage();
            message.TextFormat = TextFormatTypes.Plain;
            switch (questionNumber)
            {
                case 1:
                    message.SuggestedActions = new SuggestedActions()
                    {
                        Actions = new List<CardAction>()
                        {
                            new CardAction() { Title = "27", Type = ActionTypes.ImBack, Value = "27"},
                            new CardAction() { Title = "72", Type = ActionTypes.ImBack, Value = "72"}
                        }
                    };
                    message.Text = "What is 24+3?";
                    await context.PostAsync(message);
                    break;
                case 2:
                    message.SuggestedActions = new SuggestedActions()
                    {
                        Actions = new List<CardAction>()
                        {
                            new CardAction() { Title = "278", Type = ActionTypes.ImBack, Value = "278"},
                            new CardAction() { Title = "287", Type = ActionTypes.ImBack, Value = "287"}
                        }
                    };
                    message.Text = "What is 41*7?";
                    await context.PostAsync(message);
                    break;
                case 3:
                    message.SuggestedActions = new SuggestedActions()
                    {
                        Actions = new List<CardAction>()
                        {
                            new CardAction() { Title = "961", Type = ActionTypes.ImBack, Value = "961"},
                            new CardAction() { Title = "691", Type = ActionTypes.ImBack, Value = "691"}
                        }
                    };
                    message.Text = "What is 1922/2?";
                    await context.PostAsync(message);
                    break;
                case 4:
                    message.SuggestedActions = new SuggestedActions()
                    {
                        Actions = new List<CardAction>()
                        {
                            new CardAction() { Title = "164", Type = ActionTypes.ImBack, Value = "164"},
                            new CardAction() { Title = "184", Type = ActionTypes.ImBack, Value = "184"}
                        }
                    };
                    message.Text = "What is 253-89?";
                    await context.PostAsync(message);
                    break;
            }
        }

        private async Task ResumeAfterUserName(IDialogContext context, IAwaitable<string> result)
        {
            try
            {
                var userName = await result;

                await context.PostAsync($"Welcome {userName}");

                context.UserData.SetValue(ContextConstants.UserName, userName);

                int questionsAsked;
                if (!context.ConversationData.TryGetValue(nameof(ContextConstants.QuestionsAsked), out questionsAsked))
                {
                    context.ConversationData.SetValue(nameof(ContextConstants.QuestionsAsked), 0);
                    context.ConversationData.SetValue(nameof(ContextConstants.Score), 0);

                    await AskQuestion(context, 1);
                    context.ConversationData.SetValue(nameof(ContextConstants.QuestionsAsked), 1);
                }
            }
            catch(TooManyAttemptsException)
            {
            }
            context.Wait(this.MessageReceivedAsync);
        }

    }
}