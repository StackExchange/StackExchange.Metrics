using System;
using System.Reflection;

namespace StackExchange.Metrics.Infrastructure
{
    class MetricTag
    {
        public readonly string Name;
        public readonly bool IsFromDefault;
        public readonly bool IsOptional;
        public readonly FieldInfo FieldInfo;
        public readonly MetricTagAttribute Attribute;

        /// <summary>
        /// Only use this constructor when creating a default tag.
        /// </summary>
        public MetricTag(string name)
        {
            Name = name;
            IsFromDefault = true;
            IsOptional = false;
        }

        /// <summary>
        /// Use this constructor when instantiating from a field.
        /// </summary>
        public MetricTag(FieldInfo fieldInfo, MetricTagAttribute attribute, Func<string, string> nameReplacer)
        {
            IsFromDefault = false;
            IsOptional = attribute.IsOptional;

            FieldInfo = fieldInfo;
            if (!FieldInfo.IsInitOnly || (FieldInfo.FieldType != typeof(string) && !FieldInfo.FieldType.IsEnum))
            {
                throw new InvalidOperationException(
                    $"The BosunTag attribute can only be applied to readonly string or enum fields. {fieldInfo.DeclaringType.FullName}.{fieldInfo.Name} is invalid.");
            }

            Attribute = attribute;

            if (attribute.Name != null)
                Name = attribute.Name;
            else if (nameReplacer != null)
                Name = nameReplacer(fieldInfo.Name);
            else
                Name = fieldInfo.Name;

            if (!MetricValidation.IsValidTagName(Name))
            {
                throw new InvalidOperationException($"\"{Name}\" is not a valid Bosun Tag name. Field: {fieldInfo.DeclaringType.FullName}.{fieldInfo.Name}.");
            }
        }
    }
}
