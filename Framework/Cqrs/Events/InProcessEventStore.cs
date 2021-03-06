﻿#region Copyright
// // -----------------------------------------------------------------------
// // <copyright company="Chinchilla Software Limited">
// // 	Copyright Chinchilla Software Limited. All rights reserved.
// // </copyright>
// // -----------------------------------------------------------------------
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using Cqrs.Domain;
using Cqrs.Messages;

namespace Cqrs.Events
{
	/// <summary>
	/// An <see cref="IEventStore{TAuthenticationToken}"/> that uses a local (non-static) <see cref="IDictionary{TKey,TValue}"/>.
	/// This does not manage memory in any way and will continue to grow. Mostly suitable for running tests or short lived processes.
	/// </summary>
	/// <typeparam name="TAuthenticationToken">The <see cref="Type"/> of the authentication token.</typeparam>
	public class InProcessEventStore<TAuthenticationToken> : IEventStore<TAuthenticationToken>
	{
		/// <summary>
		/// Gets or sets the in-memory storage <see cref="IDictionary{TKey,TValue}"/>.
		/// </summary>
		protected IDictionary<Guid, IList<IEvent<TAuthenticationToken>>> InMemoryDb { get; private set; }

		/// <summary>
		/// Instantiate a new instance of the <see cref="InProcessEventStore{TAuthenticationToken}"/> class.
		/// </summary>
		public InProcessEventStore()
		{
			InMemoryDb = new Dictionary<Guid, IList<IEvent<TAuthenticationToken>>>();
		}

		/// <summary>
		/// Saves the provided <paramref name="event"/>.
		/// </summary>
		/// <param name="aggregateRootType"> <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</param>
		/// <param name="event">The <see cref="IEvent{TAuthenticationToken}"/> to be saved.</param>
		public void Save(Type aggregateRootType, IEvent<TAuthenticationToken> @event)
		{
			IList<IEvent<TAuthenticationToken>> list;
			InMemoryDb.TryGetValue(@event.GetIdentity(), out list);
			if (list == null)
			{
				list = new List<IEvent<TAuthenticationToken>>();
				InMemoryDb.Add(@event.GetIdentity(), list);
			}
			list.Add(@event);
		}

		/// <summary>
		/// Gets a collection of <see cref="IEvent{TAuthenticationToken}"/> for the <typeparamref name="T">aggregate root</typeparamref> with the ID matching the provided <paramref name="aggregateId"/>.
		/// </summary>
		/// <typeparam name="T">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</typeparam>
		/// <param name="aggregateId">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="useLastEventOnly">Loads only the last event<see cref="IEvent{TAuthenticationToken}"/>.</param>
		/// <param name="fromVersion">Load events starting from this version</param>
		public IEnumerable<IEvent<TAuthenticationToken>> Get<T>(Guid aggregateId, bool useLastEventOnly = false, int fromVersion = -1)
		{
			return Get(typeof(T), aggregateId, useLastEventOnly, fromVersion);
		}

		/// <summary>
		/// Gets a collection of <see cref="IEvent{TAuthenticationToken}"/> for the <see cref="IAggregateRoot{TAuthenticationToken}"/> of type <paramref name="aggregateType"/> with the ID matching the provided <paramref name="aggregateId"/>.
		/// </summary>
		/// <param name="aggregateType"> <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</param>
		/// <param name="aggregateId">The <see cref="IAggregateRoot{TAuthenticationToken}.Id"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/>.</param>
		/// <param name="useLastEventOnly">Loads only the last event<see cref="IEvent{TAuthenticationToken}"/>.</param>
		/// <param name="fromVersion">Load events starting from this version</param>
		public IEnumerable<IEvent<TAuthenticationToken>> Get(Type aggregateType, Guid aggregateId, bool useLastEventOnly = false, int fromVersion = -1)
		{
			IList<IEvent<TAuthenticationToken>> events;
			InMemoryDb.TryGetValue(aggregateId, out events);
			return events != null
				? events.Where(x => x.Version > fromVersion)
				: new List<IEvent<TAuthenticationToken>>();
		}

		/// <summary>
		/// Get all <see cref="IEvent{TAuthenticationToken}"/> instances for the given <paramref name="correlationId"/>.
		/// </summary>
		/// <param name="correlationId">The <see cref="IMessage.CorrelationId"/> of the <see cref="IEvent{TAuthenticationToken}"/> instances to retrieve.</param>
		public IEnumerable<EventData> Get(Guid correlationId)
		{
			return Enumerable.Empty<EventData>();
		}

		/// <summary>
		/// Saves the provided <paramref name="event"/>.
		/// </summary>
		/// <typeparam name="T">The <see cref="Type"/> of the <see cref="IAggregateRoot{TAuthenticationToken}"/> the <see cref="IEvent{TAuthenticationToken}"/> was raised in.</typeparam>
		/// <param name="event">The <see cref="IEvent{TAuthenticationToken}"/> to be saved.</param>
		public void Save<T>(IEvent<TAuthenticationToken> @event)
		{
			Save(typeof(T), @event);
		}
	}
}