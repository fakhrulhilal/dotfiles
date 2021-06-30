using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UnitTests
{
    internal class ClassComparer
    {
        internal static IComparer ByPublicProperty => new CompareObjectPublicProperty();

        internal static IComparer ByPublicPropertyExcept(params string[] excludedProperties) =>
            new CompareObjectPublicProperty(excludedProperties);

        internal class CompareObjectPublicProperty : IComparer
        {
            private readonly string[] _ignoredProperties;
            private readonly bool _useEnumAsString;

            public CompareObjectPublicProperty(params string[] ignore)
            {
                _ignoredProperties = ignore ?? new[] { string.Empty };
            }

            public CompareObjectPublicProperty(bool useEnumAsString, params string[] ignore)
            {
                _useEnumAsString = useEnumAsString;
                _ignoredProperties = ignore;
            }

            public int Compare(object source, object target)
            {
                if (source == null || target == null) return source == target ? 0 : 1;

                var properties =
                    source.GetType()
                        .GetProperties(BindingFlags.Public | BindingFlags.Instance);
            
                var targetType = target.GetType();
                if (!properties.Any()) return source == target ? 0 : 1;

                var ignoreList = new List<string>(_ignoredProperties);
                var collectionType = typeof(IEnumerable);
                foreach (var prop in properties)
                {
                    if (ignoreList.Contains(prop.Name)) continue;

                    var sourceValue = prop.GetValue(source, null);
                    var targetValue = targetType.GetProperty(prop.Name)?.GetValue(target, null);
                    if (prop.PropertyType.IsEnum && _useEnumAsString)
                    {
                        sourceValue = sourceValue.ToString();
                        targetValue = targetValue?.ToString();
                    }

                    if (collectionType.IsAssignableFrom(prop.PropertyType))
                    {
                        var sourceCollection = sourceValue as IEnumerable;
                        var targetCollection = targetValue as IEnumerable;
                        if (sourceCollection == null || targetCollection == null) return 1;

                        var targetEnumerator = targetCollection.GetEnumerator();
                        foreach (var sourceItem in sourceCollection)
                        {
                            if (!targetEnumerator.MoveNext()) return 1;
                            if (IsNotEqual(sourceItem, targetEnumerator.Current)) return 1;
                        }

                        if (targetEnumerator.MoveNext()) return 1;
                    }
                    else if (IsNotEqual(sourceValue, targetValue)) return 1;
                }
                return 0;
            }

            private bool IsNotEqual(object sourceValue, object targetValue) =>
                sourceValue != targetValue && (sourceValue == null || !sourceValue.Equals(targetValue));
        }
    }
}
