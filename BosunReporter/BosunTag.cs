using System;
using System.Reflection;

namespace BosunReporter
{
    internal class BosunTag
    {
        public readonly string Name;
        public readonly FieldInfo FieldInfo;
        public readonly BosunTagAttribute Attribute;

        public BosunTag(MemberInfo memberInfo, BosunTagAttribute attribute)
        {
            if (!(memberInfo is FieldInfo))
            {
                throw new InvalidOperationException(
                    String.Format("The BosunTag attribute can only be applied to readonly string fields. Not properties or methods. {0}.{1} is invalid.",
                        memberInfo.DeclaringType.FullName, memberInfo.Name));
            }

            FieldInfo = (FieldInfo)memberInfo;
            if (!FieldInfo.IsInitOnly || FieldInfo.FieldType != typeof(string))
            {
                throw new InvalidOperationException(
                    String.Format("The BosunTag attribute can only be applied to readonly string fields. {0}.{1} is invalid.",
                        memberInfo.DeclaringType.FullName, memberInfo.Name));
            }

            Attribute = attribute;

            Name = attribute.Name ?? memberInfo.Name;
            if (!Validation.IsValidTagName(Name))
                throw new InvalidOperationException(Name + " is not a valid Bosun Tag name.");
        }
    }
}
