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

    [AttributeUsage(AttributeTargets.Class)]
    public class ExcludeDefaultTagsAttribute : Attribute
    {
        public string[] Tags { get; }

        /// <summary>
        /// Excludes default tags.
        /// </summary>
        /// <param name="tags">List of default tags to exclude. Leave empty to exclude all defaults.</param>
        public ExcludeDefaultTagsAttribute(params string[] tags)
        {
            Tags = tags;
        }
    }
    
    [AttributeUsage(AttributeTargets.Class)]
    public class RestoreDefaultTagsAttribute : Attribute
    {
        public string[] Tags { get; }

        /// <summary>
        /// Includes default tags which may have been excluded by a base class.
        /// </summary>
        /// <param name="tags">List of default tags to restore. Leave empty to restore all excluded default tags.</param>
        public RestoreDefaultTagsAttribute(params string[] tags)
        {
            Tags = tags;
        }
    }
}