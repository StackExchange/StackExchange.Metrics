using System;
using BosunReporter.Metrics;

namespace BosunReporter
{
    /// <summary>
    /// Marks a field as a Bosun tag.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class BosunTagAttribute : Attribute
    {
        /// <summary>
        /// An explicit name for the tag. By default, the field name controls the tag name.
        /// </summary>
        public readonly string Name;
        /// <summary>
        /// If true, the tag is considered optional. Null values will not be serialized.
        /// </summary>
        public readonly bool IsOptional;

        /// <summary>
        /// Marks a field as a Bosun tag.
        /// </summary>
        /// <param name="name">An explicit name for the tag. By default, the field name controls the tag name.</param>
        /// <param name="isOptional">If true, the tag is considered optional. Null values will not be serialized.</param>
        public BosunTagAttribute(string name = null, bool isOptional = false)
        {
            Name = name;
            IsOptional = isOptional;
        }
    }

    /// <summary>
    /// Excludes default tags from a BosunMetric. The primary use case is <see cref="ExternalCounter"/> which excludes the Host tag.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ExcludeDefaultTagsAttribute : Attribute
    {
        /// <summary>
        /// Array of default tags to exclude. If empty, all default tags are ignored.
        /// </summary>
        public string[] Tags { get; }

        /// <summary>
        /// Excludes default tags.
        /// </summary>
        /// <param name="tags">Array of default tags to exclude. Leave empty to exclude all defaults.</param>
        public ExcludeDefaultTagsAttribute(params string[] tags)
        {
            Tags = tags;
        }
    }

    /// <summary>
    /// Includes default tags which may have been excluded by a base class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class RestoreDefaultTagsAttribute : Attribute
    {
        /// <summary>
        /// Array of default tags to restore. If empty, all default tags are restored.
        /// </summary>
        public string[] Tags { get; }

        /// <summary>
        /// Includes default tags which may have been excluded by a base class.
        /// </summary>
        /// <param name="tags">Array of default tags to restore. Leave empty to restore all excluded default tags.</param>
        public RestoreDefaultTagsAttribute(params string[] tags)
        {
            Tags = tags;
        }
    }
}