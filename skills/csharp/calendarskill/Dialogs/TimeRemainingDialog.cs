﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CalendarSkill.Models;
using CalendarSkill.Models.ActionInfos;
using CalendarSkill.Responses.Shared;
using CalendarSkill.Responses.TimeRemaining;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Solutions.Extensions;
using Microsoft.Bot.Solutions.Resources;
using Microsoft.Bot.Solutions.Skills;
using Microsoft.Bot.Solutions.Util;

namespace CalendarSkill.Dialogs
{
    public class TimeRemainingDialog : CalendarSkillDialogBase
    {
        public TimeRemainingDialog(
            IServiceProvider serviceProvider)
            : base(nameof(TimeRemainingDialog), serviceProvider)
        {
            var timeRemain = new WaterfallStep[]
            {
                GetAuthTokenAsync,
                AfterGetAuthTokenAsync,
                CheckTimeRemainAsync,
            };

            // Define the conversation flow using a waterfall model.
            AddDialog(new WaterfallDialog(Actions.ShowTimeRemaining, timeRemain) { TelemetryClient = TelemetryClient });

            // Set starting dialog for component
            InitialDialogId = Actions.ShowTimeRemaining;
        }

        private async Task<DialogTurnResult> CheckTimeRemainAsync(WaterfallStepContext sc, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                var state = await Accessor.GetAsync(sc.Context, cancellationToken: cancellationToken);
                sc.Context.TurnState.TryGetValue(StateProperties.APITokenKey, out var token);

                var calendarService = ServiceManager.InitCalendarService(token as string, state.EventSource);

                var eventList = await calendarService.GetUpcomingEventsAsync();
                var nextEventList = new List<EventModel>();
                foreach (var item in eventList)
                {
                    var itemUserTimeZoneTime = TimeZoneInfo.ConvertTime(item.StartTime, TimeZoneInfo.Utc, state.GetUserTimeZone());
                    if (item.IsCancelled != true && nextEventList.Count == 0)
                    {
                        if (state.MeetingInfo.OrderReference.ToLower().Contains(CalendarCommonStrings.Next))
                        {
                            nextEventList.Add(item);
                        }
                        else if (state.MeetingInfo.StartDate.Any() && itemUserTimeZoneTime.DayOfYear == state.MeetingInfo.StartDate[0].DayOfYear)
                        {
                            nextEventList.Add(item);
                        }
                        else if (state.MeetingInfo.StartTime.Any() && itemUserTimeZoneTime == state.MeetingInfo.StartTime[0])
                        {
                            nextEventList.Add(item);
                        }
                        else if (state.MeetingInfo.Title != null && item.Title.Equals(state.MeetingInfo.Title, StringComparison.CurrentCultureIgnoreCase))
                        {
                            nextEventList.Add(item);
                        }
                    }
                }

                var totalRemainingMinutes = 0;
                var status = false;
                if (nextEventList.Count == 0)
                {
                    var prompt = TemplateManager.GenerateActivityForLocale(TimeRemainingResponses.ShowNoMeetingMessage);
                    await sc.Context.SendActivityAsync(prompt, cancellationToken);
                }
                else
                {
                    var userTimeZone = state.GetUserTimeZone();
                    var timeNow = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.Local, userTimeZone);
                    var timeDiff = TimeZoneInfo.ConvertTime(nextEventList[0].StartTime, TimeZoneInfo.Utc, userTimeZone) - timeNow;
                    var timeDiffMinutes = (int)timeDiff.TotalMinutes % 60;
                    var timeDiffHours = (int)timeDiff.TotalMinutes / 60;
                    var timeDiffDays = timeDiff.Days;
                    totalRemainingMinutes = (int)timeDiff.TotalMinutes;
                    status = true;

                    var remainingMinutes = string.Empty;
                    var remainingHours = string.Empty;
                    var remainingDays = string.Empty;

                    if (timeDiffMinutes > 0)
                    {
                        if (timeDiffMinutes > 1)
                        {
                            remainingMinutes = string.Format(CommonStrings.TimeFormatMinutes, timeDiffMinutes) + " ";
                        }
                        else
                        {
                            remainingMinutes = string.Format(CommonStrings.TimeFormatMinute, timeDiffMinutes) + " ";
                        }
                    }

                    if (timeDiffHours > 0)
                    {
                        if (timeDiffHours > 1)
                        {
                            remainingHours = string.Format(CommonStrings.TimeFormatHours, timeDiffHours) + " ";
                        }
                        else
                        {
                            remainingHours = string.Format(CommonStrings.TimeFormatHour, timeDiffHours) + " ";
                        }
                    }

                    if (timeDiffDays > 0)
                    {
                        if (timeDiffDays > 1)
                        {
                            remainingDays = string.Format(CommonStrings.TimeFormatDays, timeDiffDays) + " ";
                        }
                        else
                        {
                            remainingDays = string.Format(CommonStrings.TimeFormatDay, timeDiffDays) + " ";
                        }
                    }

                    var remainingTime = $"{remainingDays}{remainingHours}{remainingMinutes}";
                    if (state.MeetingInfo.OrderReference == "next")
                    {
                        var tokens = new
                        {
                            RemainingTime = remainingTime
                        };
                        var prompt = TemplateManager.GenerateActivityForLocale(TimeRemainingResponses.ShowNextMeetingTimeRemainingMessage, tokens);
                        await sc.Context.SendActivityAsync(prompt, cancellationToken);
                    }
                    else
                    {
                        var timeToken = string.Empty;
                        var timeSpeakToken = string.Empty;

                        if (state.MeetingInfo.StartDate.Any())
                        {
                            timeSpeakToken += $"{state.MeetingInfo.StartDate[0].ToSpeechDateString()} ";
                            timeToken += $"{state.MeetingInfo.StartDate[0].ToShortDateString()} ";
                        }

                        if (state.MeetingInfo.StartTime.Any())
                        {
                            timeSpeakToken += $"{state.MeetingInfo.StartTime[0].ToSpeechTimeString()}";
                            timeToken += $"{state.MeetingInfo.StartTime[0].ToShortTimeString()}";
                        }

                        var tokens = new
                        {
                            RemainingTime = remainingTime,
                            TimeSpeak = timeSpeakToken.Length > 0 ? CommonStrings.SpokenTimePrefix_One + " " + timeSpeakToken : string.Empty,
                            Time = timeToken.Length > 0 ? CommonStrings.SpokenTimePrefix_One + " " + timeToken : string.Empty,
                            Title = state.MeetingInfo.Title != null ? string.Format(CalendarCommonStrings.WithTheSubject, state.MeetingInfo.Title) : string.Empty
                        };

                        var prompt = TemplateManager.GenerateActivityForLocale(TimeRemainingResponses.ShowTimeRemainingMessage, tokens);
                        await sc.Context.SendActivityAsync(prompt, cancellationToken);
                    }
                }

                if (state.IsAction)
                {
                    return await sc.EndDialogAsync(new TimeRemainingOutput() { RemainingTime = totalRemainingMinutes, ActionSuccess = status }, cancellationToken);
                }

                return await sc.EndDialogAsync(cancellationToken: cancellationToken);
            }
            catch (SkillException ex)
            {
                await HandleDialogExceptionsAsync(sc, ex, cancellationToken);
                return new DialogTurnResult(DialogTurnStatus.Cancelled, CommonUtil.DialogTurnResultCancelAllDialogs);
            }
            catch (Exception ex)
            {
                await HandleDialogExceptionsAsync(sc, ex, cancellationToken);
                return new DialogTurnResult(DialogTurnStatus.Cancelled, CommonUtil.DialogTurnResultCancelAllDialogs);
            }
        }
    }
}