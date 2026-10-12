using System;
using System.Globalization;
using System.IO;
using IceP.Application.Persistence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IceP.Infrastructure.Persistence
{
    internal sealed class SaveFormatException : Exception
    {
        public PersistenceError Error { get; }
        public SaveFormatException(string message, PersistenceError error = PersistenceError.InvalidData) : base(message)
        { Error = error; }
    }

    internal sealed class SaveJsonCodec
    {
        private readonly SaveDataValidator validator;
        private static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings
        {
            DateFormatString = "O", Culture = CultureInfo.InvariantCulture,
            TypeNameHandling = TypeNameHandling.None, FloatFormatHandling = FloatFormatHandling.String
        });

        public SaveJsonCodec(SaveDataValidator validator) { this.validator = validator; }

        public string Encode(SaveData snapshot)
        {
            var errors = validator.Validate(snapshot);
            if (errors.Count > 0) throw new SaveFormatException(string.Join("\n", errors),
                snapshot != null && snapshot.SaveVersion != SaveData.CurrentVersion ? PersistenceError.UnsupportedVersion : PersistenceError.InvalidData);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using (var writer = new JsonTextWriter(output) { Formatting = Formatting.Indented })
                Serializer().Serialize(writer, snapshot);
            return output.ToString();
        }

        public SaveData Decode(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 16 };
            var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new SaveFormatException("Unexpected content after save object.");
            var version = root[nameof(SaveData.SaveVersion)];
            if (version?.Type == JTokenType.Integer && version.Value<long>() != SaveData.CurrentVersion)
                throw new SaveFormatException($"Unsupported SaveVersion {version}.", PersistenceError.UnsupportedVersion);
            var settingsVersion = (root["Settings"] as JObject)?["SettingsVersion"];
            if (settingsVersion?.Type == JTokenType.Integer && settingsVersion.Value<long>() != 1)
                throw new SaveFormatException($"Unsupported SettingsVersion {settingsVersion}.", PersistenceError.UnsupportedVersion);
            ValidateShape(root, typeof(SaveData), "SaveData");
            var data = root.ToObject<SaveData>(Serializer());
            var errors = validator.Validate(data);
            if (errors.Count > 0) throw new SaveFormatException(string.Join("\n", errors));
            return data;
        }

        private static void ValidateShape(JObject obj, Type type, string path)
        {
            foreach (var property in type.GetProperties())
            {
                var field = obj[property.Name];
                var label = path + "." + property.Name;
                bool optionalEvent = (type == typeof(SaveData) || type == typeof(PendingSpin)) &&
                    property.Name == nameof(SaveData.JackpotEventId);
                if (field == null && optionalEvent) continue;
                if (field == null) throw new SaveFormatException(label + " is missing.");
                var nullable = Nullable.GetUnderlyingType(property.PropertyType);
                var fieldType = nullable ?? property.PropertyType;
                if (field.Type == JTokenType.Null)
                {
                    if (nullable != null || fieldType == typeof(PendingSpin) || optionalEvent) continue;
                    throw new SaveFormatException(label + " cannot be null.");
                }
                bool valid;
                if (fieldType == typeof(UserSettings) || fieldType == typeof(PendingSpin))
                {
                    if (!(field is JObject child)) throw new SaveFormatException(label + " must be an object.");
                    ValidateShape(child, fieldType, label);
                    continue;
                }
                if (fieldType == typeof(long) || fieldType == typeof(int) || fieldType.IsEnum)
                {
                    valid = field.Type == JTokenType.Integer && long.TryParse(field.ToString(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var number) &&
                        (fieldType == typeof(long) || (number >= int.MinValue && number <= int.MaxValue));
                }
                else if (fieldType == typeof(double)) valid = field.Type == JTokenType.Integer || field.Type == JTokenType.Float;
                else if (fieldType == typeof(bool)) valid = field.Type == JTokenType.Boolean;
                else if (fieldType == typeof(Guid)) valid = field.Type == JTokenType.String &&
                    Guid.TryParseExact(field.Value<string>(), "D", out _);
                else if (fieldType == typeof(DateTimeOffset)) valid = field.Type == JTokenType.String &&
                    DateTimeOffset.TryParseExact(field.Value<string>(), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
                else valid = field.Type == JTokenType.String;
                if (!valid) throw new SaveFormatException(label + " has a malformed or unsupported serialized value.");
            }
            foreach (var property in obj.Properties())
                if (type.GetProperty(property.Name) == null) throw new SaveFormatException(path + "." + property.Name + " is unsupported.");
        }
    }
}
