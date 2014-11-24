using System;

namespace BosunReporter
{
    [AttributeUsage(AttributeTargets.Field)]
    public class BosunTagAttribute : Attribute
    {
        public readonly string Name;
        public readonly bool IsOptional;

        public BosunTagAttribute(string name = null, bool isOptional = false)
        {
            Name = name;
            IsOptional = isOptional;
        }
    }
}
