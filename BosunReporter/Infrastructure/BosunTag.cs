using System;
using System.Reflection;

namespace BosunReporter.Infrastructure
{
    internal class BosunTag
    {
        public readonly string Name;
        public readonly bool IsFromDefault;
        public readonly bool IsOptional;
        public readonly FieldInfo FieldInfo;
        public readonly BosunTagAttribute Attribute;

        /// <summary>
        /// Only use this constructor when creating a default tag.
        /// </summary>
        public BosunTag(string name)
        {
            Name = name;
            IsFromDefault = true;
            IsOptional = false;
        }

        /// <summary>
        /// Use this constructor when instantiating from a field.
        /// </summary>
        public BosunTag(FieldInfo fieldInfo, BosunTagAttribute attribute, Func<string, string> nameReplacer)
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

            if (!BosunValidation.IsValidTagName(Name))
            {
                throw new InvalidOperationException($"\"{Name}\" is not a valid Bosun Tag name. Field: {fieldInfo.DeclaringType.FullName}.{fieldInfo.Name}.");
            }
        }
    }
}
