﻿#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using cdmdotnet.Logging;
using Cqrs.Authentication;
using Cqrs.Bus;
using Cqrs.Configuration;
using Cqrs.Events;
using Microsoft.ServiceBus.Messaging;
using Cqrs.Messages;

namespace Cqrs.Azure.ServiceBus
{
	/// <summary>
	/// An <see cref="IEventPublisher{TAuthenticationToken}"/> that resolves handlers and executes the handler.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	// The “,nq” suffix here just asks the expression evaluator to remove the quotes when displaying the final value (nq = no quotes).
	[DebuggerDisplay("{DebuggerDisplay,nq}")]
	public class AzureEventBusPublisher<TAuthenticationToken>
		: AzureEventBus<TAuthenticationToken>
		, IEventPublisher<TAuthenticationToken>
	{
		/// <summary>
		/// Instantiates a new instance of <see cref="AzureEventBusPublisher{TAuthenticationToken}"/>.
		/// </summary>
		public AzureEventBusPublisher(IConfigurationManager configurationManager, IMessageSerialiser<TAuthenticationToken> messageSerialiser, IAuthenticationTokenHelper<TAuthenticationToken> authenticationTokenHelper, ICorrelationIdHelper correlationIdHelper, ILogger logger, IAzureBusHelper<TAuthenticationToken> azureBusHelper, IBusHelper busHelper)
			: base(configurationManager, messageSerialiser, authenticationTokenHelper, correlationIdHelper, logger, azureBusHelper, busHelper, true)
		{
			TelemetryHelper = configurationManager.CreateTelemetryHelper("Cqrs.Azure.EventBus.Publisher.UseApplicationInsightTelemetryHelper", correlationIdHelper);
		}

		/// <summary>
		/// The debugger variable window value.
		/// </summary>
		internal string DebuggerDisplay
		{
			get
			{
				string connectionString = string.Format("ConnectionString : {0}", MessageBusConnectionStringConfigurationKey);
				try
				{
					connectionString = string.Concat(connectionString, "=", GetConnectionString());
				}
				catch { /* */ }
				return string.Format(CultureInfo.InvariantCulture, "{0}, PrivateTopicName : {1}, PrivateTopicSubscriptionName : {2}, PublicTopicName : {3}, PublicTopicSubscriptionName : {4}",
					connectionString, PrivateTopicName, PrivateTopicSubscriptionName, PublicTopicName, PublicTopicSubscriptionName);
			}
		}

		#region Implementation of IEventPublisher<TAuthenticationToken>

		/// <summary>
		/// Publishes the provided <paramref name="event"/> on the event bus.
		/// </summary>
		public virtual void Publish<TEvent>(TEvent @event)
			where TEvent : IEvent<TAuthenticationToken>
		{
			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = null;
			bool mainWasSuccessfull = false;
			bool telemeterOverall = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "Azure/Servicebus" } };
			string telemetryName = string.Format("{0}/{1}/{2}", @event.GetType().FullName, @event.GetIdentity(), @event.Id);
			var telemeteredEvent = @event as ITelemeteredMessage;
			if (telemeteredEvent != null)
				telemetryName = telemeteredEvent.TelemetryName;
			telemetryName = string.Format("Event/{0}", telemetryName);

			try
			{
				if (!AzureBusHelper.PrepareAndValidateEvent(@event, "Azure-ServiceBus"))
					return;

				var privateEventAttribute = Attribute.GetCustomAttribute(typeof(TEvent), typeof(PrivateEventAttribute)) as PrivateEventAttribute;
				var publicEventAttribute = Attribute.GetCustomAttribute(typeof(TEvent), typeof(PrivateEventAttribute)) as PublicEventAttribute;

				// We only add telemetry for overall operations if two occured
				telemeterOverall = publicEventAttribute != null && privateEventAttribute != null;

				// Backwards compatibility and simplicity
				bool wasSuccessfull;
				Stopwatch stopWatch = Stopwatch.StartNew();
				if (publicEventAttribute == null && privateEventAttribute == null)
				{
					stopWatch.Restart();
					responseCode = "200";
					wasSuccessfull = false;
					try
					{
						var brokeredMessage = new BrokeredMessage(MessageSerialiser.SerialiseEvent(@event))
						{
							CorrelationId = CorrelationIdHelper.GetCorrelationId().ToString("N")
						};
						brokeredMessage.Properties.Add("Type", @event.GetType().FullName);
						PublicServiceBusPublisher.Send(brokeredMessage);
						wasSuccessfull = true;
					}
					catch (QuotaExceededException exception)
					{
						responseCode = "429";
						Logger.LogError("The size of the event being sent was too large or the topic has reached it's limit.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					catch (Exception exception)
					{
						responseCode = "500";
						Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					finally
					{
						TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Default Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
					}
					Logger.LogDebug(string.Format("An event was published on the public bus with the id '{0}' was of type {1}.", @event.Id, @event.GetType().FullName));
				}
				if (publicEventAttribute != null)
				{
					stopWatch.Restart();
					responseCode = "200";
					wasSuccessfull = false;
					try
					{
						var brokeredMessage = new BrokeredMessage(MessageSerialiser.SerialiseEvent(@event))
						{
							CorrelationId = CorrelationIdHelper.GetCorrelationId().ToString("N")
						};
						brokeredMessage.Properties.Add("Type", @event.GetType().FullName);
						PublicServiceBusPublisher.Send(brokeredMessage);
						wasSuccessfull = true;
					}
					catch (QuotaExceededException exception)
					{
						responseCode = "429";
						Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					catch (Exception exception)
					{
						responseCode = "500";
						Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					finally
					{
						TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Public Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
					}
					Logger.LogDebug(string.Format("An event was published on the public bus with the id '{0}' was of type {1}.", @event.Id, @event.GetType().FullName));
				}
				if (privateEventAttribute != null)
				{
					stopWatch.Restart();
					responseCode = "200";
					wasSuccessfull = false;
					try
					{
						var brokeredMessage = new BrokeredMessage(MessageSerialiser.SerialiseEvent(@event))
						{
							CorrelationId = CorrelationIdHelper.GetCorrelationId().ToString("N")
						};
						brokeredMessage.Properties.Add("Type", @event.GetType().FullName);
						PrivateServiceBusPublisher.Send(brokeredMessage);
						wasSuccessfull = true;
					}
					catch (QuotaExceededException exception)
					{
						responseCode = "429";
						Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					catch (Exception exception)
					{
						responseCode = "500";
						Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Event", @event } });
						throw;
					}
					finally
					{
						TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Private Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
					}

					Logger.LogDebug(string.Format("An event was published on the private bus with the id '{0}' was of type {1}.", @event.Id, @event.GetType().FullName));
				}
				mainWasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				if (telemeterOverall)
					TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, mainWasSuccessfull, telemetryProperties);
			}
		}

		/// <summary>
		/// Publishes the provided <paramref name="events"/> on the event bus.
		/// </summary>
		public virtual void Publish<TEvent>(IEnumerable<TEvent> events)
			where TEvent : IEvent<TAuthenticationToken>
		{
			IList<TEvent> sourceEvents = events.ToList();

			DateTimeOffset startedAt = DateTimeOffset.UtcNow;
			Stopwatch mainStopWatch = Stopwatch.StartNew();
			string responseCode = null;
			bool mainWasSuccessfull = false;

			IDictionary<string, string> telemetryProperties = new Dictionary<string, string> { { "Type", "Azure/Servicebus" } };
			string telemetryName = "Events";
			string telemetryNames = string.Empty;
			foreach (TEvent @event in sourceEvents)
			{
				string subTelemetryName = string.Format("{0}/{1}/{2}", @event.GetType().FullName, @event.GetIdentity(), @event.Id);
				var telemeteredEvent = @event as ITelemeteredMessage;
				if (telemeteredEvent != null)
					subTelemetryName = telemeteredEvent.TelemetryName;
				telemetryNames = string.Format("{0}{1},", telemetryNames, subTelemetryName);
			}
			if (telemetryNames.Length > 0)
				telemetryNames = telemetryNames.Substring(0, telemetryNames.Length - 1);
			telemetryProperties.Add("Events", telemetryNames);

			try
			{
				IList<string> sourceEventMessages = new List<string>();
				IList<BrokeredMessage> privateBrokeredMessages = new List<BrokeredMessage>(sourceEvents.Count);
				IList<BrokeredMessage> publicBrokeredMessages = new List<BrokeredMessage>(sourceEvents.Count);
				foreach (TEvent @event in sourceEvents)
				{
					if (!AzureBusHelper.PrepareAndValidateEvent(@event, "Azure-ServiceBus"))
						continue;

					var brokeredMessage = new BrokeredMessage(MessageSerialiser.SerialiseEvent(@event))
					{
						CorrelationId = CorrelationIdHelper.GetCorrelationId().ToString("N")
					};
					brokeredMessage.Properties.Add("Type", @event.GetType().FullName);

					var privateEventAttribute = Attribute.GetCustomAttribute(typeof(TEvent), typeof(PrivateEventAttribute)) as PrivateEventAttribute;
					var publicEventAttribute = Attribute.GetCustomAttribute(typeof(TEvent), typeof(PrivateEventAttribute)) as PublicEventAttribute;

					if
						(
						// Backwards compatibility and simplicity
						(publicEventAttribute == null && privateEventAttribute == null)
						||
						publicEventAttribute != null
						)
					{
						publicBrokeredMessages.Add(brokeredMessage);
						sourceEventMessages.Add(string.Format("An event was published on the public bus with the id '{0}' was of type {1}.", @event.Id, @event.GetType().FullName));
					}
					if (privateEventAttribute != null)
					{
						privateBrokeredMessages.Add(brokeredMessage);
						sourceEventMessages.Add(string.Format("An event was published on the private bus with the id '{0}' was of type {1}.", @event.Id, @event.GetType().FullName));
					}
				}

				bool wasSuccessfull;
				Stopwatch stopWatch = Stopwatch.StartNew();

				// Backwards compatibility and simplicity
				stopWatch.Restart();
				responseCode = "200";
				wasSuccessfull = false;
				try
				{
					PublicServiceBusPublisher.SendBatch(publicBrokeredMessages);
					wasSuccessfull = true;
				}
				catch (QuotaExceededException exception)
				{
					responseCode = "429";
					Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Events", publicBrokeredMessages } });
					throw;
				}
				catch (Exception exception)
				{
					responseCode = "500";
					Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Events", publicBrokeredMessages } });
					throw;
				}
				finally
				{
					TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Public Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
				}

				stopWatch.Restart();
				responseCode = "200";
				wasSuccessfull = false;
				try
				{
					PrivateServiceBusPublisher.SendBatch(privateBrokeredMessages);
					wasSuccessfull = true;
				}
				catch (QuotaExceededException exception)
				{
					responseCode = "429";
					Logger.LogError("The size of the event being sent was too large.", exception: exception, metaData: new Dictionary<string, object> { { "Events", privateBrokeredMessages } });
					throw;
				}
				catch (Exception exception)
				{
					responseCode = "500";
					Logger.LogError("An issue occurred while trying to publish an event.", exception: exception, metaData: new Dictionary<string, object> { { "Events", privateBrokeredMessages } });
					throw;
				}
				finally
				{
					TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, "Private Bus", startedAt, stopWatch.Elapsed, responseCode, wasSuccessfull, telemetryProperties);
				}

				foreach (string message in sourceEventMessages)
					Logger.LogInfo(message);

				mainWasSuccessfull = true;
			}
			finally
			{
				mainStopWatch.Stop();
				TelemetryHelper.TrackDependency("Azure/Servicebus/EventBus", "Event", telemetryName, null, startedAt, mainStopWatch.Elapsed, responseCode, mainWasSuccessfull, telemetryProperties);
			}
		}

		#endregion
	}
}