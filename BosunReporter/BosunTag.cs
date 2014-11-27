using System;
using System.Reflection;

namespace BosunReporter
{
    internal class BosunTag
    {
        public readonly string Name;
        public readonly FieldInfo FieldInfo;
        public readonly BosunTagAttribute Attribute;

        public BosunTag(FieldInfo fieldInfo, BosunTagAttribute attribute, Func<string, string> nameReplacer)
        {
            FieldInfo = fieldInfo;
            if (!FieldInfo.IsInitOnly || FieldInfo.FieldType != typeof(string))
            {
                throw new InvalidOperationException(
                    String.Format("The BosunTag attribute can only be applied to readonly string fields. {0}.{1} is invalid.",
                        fieldInfo.DeclaringType.FullName, fieldInfo.Name));
            }

            Attribute = attribute;

            if (attribute.Name != null)
                Name = attribute.Name;
            else if (nameReplacer != null)
                Name = nameReplacer(fieldInfo.Name);
            else
                Name = fieldInfo.Name;

            if (!Validation.IsValidTagName(Name))
            {
                throw new InvalidOperationException(
                    String.Format("\"{0}\" is not a valid Bosun Tag name. Field: {1}.{2}.",
                        Name, fieldInfo.DeclaringType.FullName, fieldInfo.Name));
            }
        }
    }
}
