﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by AsyncGenerator.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------


using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using NHibernate.DebugHelpers;
using NHibernate.Engine;
using NHibernate.Id;
using NHibernate.Linq;
using NHibernate.Loader;
using NHibernate.Persister.Collection;
using NHibernate.Type;
using NHibernate.Util;

namespace NHibernate.Collection.Generic
{
	using System.Threading.Tasks;
	using System.Threading;
	public partial class PersistentIdentifierBag<T> : AbstractPersistentCollection, IList<T>, IReadOnlyList<T>, IList, IQueryable<T>
	{

		/// <summary>
		/// Initializes this Bag from the cached values.
		/// </summary>
		/// <param name="persister">The CollectionPersister to use to reassemble the PersistentIdentifierBag.</param>
		/// <param name="disassembled">The disassembled PersistentIdentifierBag.</param>
		/// <param name="owner">The owner object.</param>
		/// <param name="cancellationToken">A cancellation token that can be used to cancel the work</param>
		public override async Task InitializeFromCacheAsync(ICollectionPersister persister, object disassembled, object owner, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			object[] array = (object[])disassembled;
			int size = array.Length;
			BeforeInitialize(persister, size);

			var identifierType = persister.IdentifierType;
			var elementType = persister.ElementType;
			for (int i = 0; i < size; i += 2)
			{
				await (identifierType.BeforeAssembleAsync(array[i], Session, cancellationToken)).ConfigureAwait(false);
				await (elementType.BeforeAssembleAsync(array[i + 1], Session, cancellationToken)).ConfigureAwait(false);
			}

			for (int i = 0; i < size; i += 2)
			{
				_identifiers[i / 2] = await (identifierType.AssembleAsync(array[i], Session, owner, cancellationToken)).ConfigureAwait(false);
				_values.Add((T) await (elementType.AssembleAsync(array[i + 1], Session, owner, cancellationToken)).ConfigureAwait(false));
			}
		}

		public override async Task<object> DisassembleAsync(ICollectionPersister persister, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			object[] result = new object[_values.Count * 2];

			int i = 0;
			for (int j = 0; j < _values.Count; j++)
			{
				object val = _values[j];
				result[i++] = await (persister.IdentifierType.DisassembleAsync(_identifiers[j], Session, null, cancellationToken)).ConfigureAwait(false);
				result[i++] = await (persister.ElementType.DisassembleAsync(val, Session, null, cancellationToken)).ConfigureAwait(false);
			}

			return result;
		}

		public override async Task<bool> EqualsSnapshotAsync(ICollectionPersister persister, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IType elementType = persister.ElementType;
			var snap = (ISet<SnapshotElement>)GetSnapshot();
			if (snap.Count != _values.Count)
			{
				return false;
			}
			for (int i = 0; i < _values.Count; i++)
			{
				object val = _values[i];
				object id = GetIdentifier(i);
				object old = snap.Where(x => Equals(x.Id, id)).Select(x => x.Value).FirstOrDefault();
				if (await (elementType.IsDirtyAsync(old, val, Session, cancellationToken)).ConfigureAwait(false))
				{
					return false;
				}
			}

			return true;
		}

		public override Task<IEnumerable> GetDeletesAsync(ICollectionPersister persister, bool indexIsFormula, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<IEnumerable>(cancellationToken);
			}
			try
			{
				return Task.FromResult<IEnumerable>(GetDeletes(persister, indexIsFormula));
			}
			catch (Exception ex)
			{
				return Task.FromException<IEnumerable>(ex);
			}
		}

		public override Task<bool> NeedsInsertingAsync(object entry, int i, IType elemType, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<bool>(cancellationToken);
			}
			try
			{
				return Task.FromResult<bool>(NeedsInserting(entry, i, elemType));
			}
			catch (Exception ex)
			{
				return Task.FromException<bool>(ex);
			}
		}

		public override async Task<bool> NeedsUpdatingAsync(object entry, int i, IType elemType, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entry == null)
			{
				return false;
			}
			var snap = (ISet<SnapshotElement>)GetSnapshot();

			object id = GetIdentifier(i);
			if (id == null)
			{
				return false;
			}

			object old = snap.Where(x => Equals(x.Id, id)).Select(x => x.Value).FirstOrDefault();
			return old != null && await (elemType.IsDirtyAsync(old, entry, Session, cancellationToken)).ConfigureAwait(false);
		}

		public override async Task<object> ReadFromAsync(DbDataReader reader, ICollectionPersister persister, ICollectionAliases descriptor, object owner, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			object element = await (persister.ReadElementAsync(reader, owner, descriptor.SuffixedElementAliases, Session, cancellationToken)).ConfigureAwait(false);
			object id = await (persister.ReadIdentifierAsync(reader, descriptor.SuffixedIdentifierAlias, Session, cancellationToken)).ConfigureAwait(false);

			// eliminate duplication if loaded in a cartesian product
			if (!_identifiers.ContainsValue(id))
			{
				_identifiers[_values.Count] = id;
				_values.Add((T) element);
			}
			return element;
		}

		//Since 5.3
		/// <inheritdoc />
		[Obsolete("This method has no more usages and will be removed in a future version")]
		public override Task<ICollection> GetOrphansAsync(object snapshot, string entityName, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<ICollection>(cancellationToken);
			}
			try
			{
				return Task.FromResult<ICollection>(GetOrphans(snapshot, entityName));
			}
			catch (Exception ex)
			{
				return Task.FromException<ICollection>(ex);
			}
		}

		public override async Task PreInsertAsync(ICollectionPersister persister, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ((persister.IdentifierGenerator as IPostInsertIdentifierGenerator) != null)
			{
				// NH Different behavior (NH specific) : if we are using IdentityGenerator the PreInsert have no effect
				return;
			}
			try
			{
				int i = 0;
				foreach (object entry in _values)
				{
					int loc = i++;
					if (!_identifiers.ContainsKey(loc)) // TODO: native ids
					{
						object id = await (persister.IdentifierGenerator.GenerateAsync(Session, entry, cancellationToken)).ConfigureAwait(false);
						_identifiers[loc] = id;
					}
				}
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception sqle)
			{
				throw new ADOException("Could not generate idbag row id.", sqle);
			}
		}
	}
}
