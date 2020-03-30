using System;
using System.Reflection;

namespace StackExchange.Metrics.Infrastructure
{
    class MetricTag
    {
        public readonly string Name;
        public readonly bool IsFromDefault;
        public readonly bool IsOptional;
        public readonly MemberInfo MemberInfo;
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
        /// Use this constructor when instantiating from a field or property.
        /// </summary>
        public MetricTag(MemberInfo memberInfo, MetricTagAttribute attribute, Func<string, string> nameReplacer)
        {
            switch (memberInfo)
            {
                case FieldInfo fieldInfo:
                    if (!fieldInfo.IsInitOnly || (fieldInfo.FieldType != typeof(string) && !fieldInfo.FieldType.IsEnum))
                    {
                        throw new InvalidOperationException(
                            $"The MetricTag attribute can only be applied to readonly string or enum fields. {memberInfo.DeclaringType.FullName}.{memberInfo.Name} is invalid."
                        );
                    }
                    break;
                case PropertyInfo propertyInfo:
                    if (propertyInfo.SetMethod != null || (propertyInfo.PropertyType != typeof(string) && !propertyInfo.PropertyType.IsEnum))
                    {
                        throw new InvalidOperationException(
                            $"The MetricTag attribute can only be applied to readonly string or enum properties. {memberInfo.DeclaringType.FullName}.{memberInfo.Name} is invalid."
                        );
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"The MetricTag attribute can only be applied to properties or fields. {memberInfo.DeclaringType.FullName}.{memberInfo.Name} is invalid."
                    );

            }

            IsFromDefault = false;
            IsOptional = attribute.IsOptional;
            MemberInfo = memberInfo;
            Attribute = attribute;

            if (attribute.Name != null)
                Name = attribute.Name;
            else if (nameReplacer != null)
                Name = nameReplacer(memberInfo.Name);
            else
                Name = memberInfo.Name;

            if (!MetricValidation.IsValidTagName(Name))
            {
                throw new InvalidOperationException($"\"{Name}\" is not a valid tag name. Field: {memberInfo.DeclaringType.FullName}.{memberInfo.Name}.");
            }
        }
    }
}
