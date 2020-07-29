﻿using System;
using System.Linq;

namespace Morestachio.Formatter.Framework
{
	public readonly struct FormatterCache
	{
		public FormatterCache(MorestachioFormatterModel model, PrepareFormatterComposingResult testedTypes)
		{
			Model = model;
			TestedTypes = testedTypes;
		}

		public MorestachioFormatterModel Model { get; }
		public PrepareFormatterComposingResult TestedTypes { get; }
	}

	public readonly struct FormatterCacheCompareKey : IEquatable<FormatterCacheCompareKey>
	{
		public FormatterCacheCompareKey(Type sourceType, FormatterArgumentType[] arguments, string name) : this()
		{
			SourceType = sourceType;
			Arguments = arguments;
			Name = name;
			_hashCode = GetHashCodeHelper();
		}

		public string Name { get; }
		public Type SourceType { get; }
		public FormatterArgumentType[] Arguments { get; }

		/// <inheritdoc />
		public bool Equals(FormatterCacheCompareKey other)
		{
			return Name == other.Name && SourceType == other.SourceType && Arguments.SequenceEqual(other.Arguments);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return obj is FormatterCacheCompareKey other && Equals(other);
		}

		private readonly int _hashCode;

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return _hashCode;
		}

		private int GetHashCodeHelper()
		{
			unchecked
			{
				var hashCode = (Name != null ? Name.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (SourceType != null ? SourceType.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^
				           (Arguments != null && Arguments.Length > 0 ? Arguments.Select(f => f.GetHashCode()).Aggregate((e, f) => e ^ f) : 0);
				return hashCode;
			}
		}
	}
}