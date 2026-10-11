using System;
using System.Collections.Generic;
using System.Linq;

namespace IceP.Domain.Configuration
{
    public sealed class ConfigValidationException : Exception
    {
        public IReadOnlyList<string> Errors { get; }

        public ConfigValidationException(IEnumerable<string> errors) : this(errors.ToArray()) { }

        private ConfigValidationException(string[] errors)
            : base("Configuration validation failed:\n- " + string.Join("\n- ", errors))
        {
            Errors = Array.AsReadOnly(errors);
        }
    }
}
