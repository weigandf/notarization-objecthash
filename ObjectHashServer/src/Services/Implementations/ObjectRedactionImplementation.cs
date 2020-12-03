﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ObjectHashServer.Exceptions;
using ObjectHashServer.Models.Extensions;
// ReSharper disable SuggestBaseTypeForParameter
// ReSharper disable PossibleNullReferenceException

namespace ObjectHashServer.Services.Implementations
{
    public class ObjectRedactionImplementation
    {
        public (JToken json, JToken salts) RedactJToken(JToken json, JToken redactSettings, JToken salts = null)
        {
            // deep clone JTokens which are changed (json object and salts)
            return RecursiveRedactDataAndSalts(json.DeepClone(), redactSettings, salts.IsNullOrEmpty() ? null : salts.DeepClone());
        }

        /// <summary>
        /// Redacts a given JSON object (JToken) for the provided redaction setting. 
        /// The redact setting can be any valid JSON with objects and arrays but as 
        /// values it can ONLY contain Booleans. For each value 'true' in the redact 
        /// settings the counterpart in the JSON will be blacked out.
        /// </summary>
        private (JToken json, JToken salts) RecursiveRedactDataAndSalts(JToken json, JToken redactSettings, JToken salts = null)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (redactSettings.Type)
            {
                case JTokenType.Boolean:
                    if (!(bool) redactSettings) return (json, salts);
                    
                    ObjectHashImplementation objectHash = new ObjectHashImplementation();
                    objectHash.HashJToken(json, salts);
                    return ("**REDACTED**" + objectHash.HashAsString(), "**REDACTED**");
                case JTokenType.Object:
                    try
                    {
                        List<string> objectKeys = ((JObject)redactSettings).Properties().Select(p => p.Name).ToList();
                        // a valid REDACT DSL command is an object with exactly one element where the this key starts with "REDACT"
                        if (objectKeys.Count == 1 && objectKeys[0].StartsWith("REDACT", Globals.STRING_COMPARE_METHOD)) // TODO: CHECK: && json.Type == JTokenType.Array)
                        {
                            return RedactJTokenWithCommand(json, (JObject)redactSettings, salts.IsNullOrEmpty() ? null : salts);
                        }

                        return RedactObject((JObject)json, (JObject)redactSettings, salts.IsNullOrEmpty() ? null : (JObject)salts);
                    }
                    catch (InvalidCastException e)
                    {
                        throw new BadRequestException("The provided JSON does not contain an object -> {} where the redact settings require one. Please check the JSON data or the redact settings.", e);
                    }
                case JTokenType.Array:
                    try {
                        return RedactArray((JArray)json, (JArray)redactSettings, salts.IsNullOrEmpty() ? null : (JArray)salts);
                    }
                    catch(InvalidCastException e)
                    {
                        throw new BadRequestException("The provided JSON does not contain an array -> [] where the redact settings require one. Please check the JSON data or the redact settings", e);
                    }
                case JTokenType.None:
                case JTokenType.Null:
                    {
                        return (json, salts);
                    }
                default:
                    throw new BadRequestException("The redact setting JSON is invalid. It can only contain a nested JSON, arrays and the data type Boolean.");
            }
        }

        private (JToken json, JToken salts) RedactObject(JObject json, JObject redactSettings, JObject salts = null)
        {
            foreach ((string key, JToken _) in redactSettings)
            {
                if (!json.ContainsKey(key) || (!salts.IsNullOrEmpty() && !salts.ContainsKey(key)))
                {
                    IDictionary additionalExceptionData = new Dictionary<string, object>
                        {
                            { "missingKey", key },
                            { "errorInObject", !json.ContainsKey(key) ? "json" : "salts" }
                        };

                    throw new BadRequestException("The provided JSON or Salt defines an object which is different from the redact settings object. Please check the JSON, the salt data or the redact settings.", additionalExceptionData);
                }

                if(salts.IsNullOrEmpty())
                {
                    (json[key], _) = RecursiveRedactDataAndSalts(json[key], redactSettings[key]);
                }
                else
                {
                    (json[key], salts[key]) = RecursiveRedactDataAndSalts(json[key], redactSettings[key], salts[key]);
                }
            }

            return (json, salts);
        }
        private (JToken json, JToken salts) RedactArray(JArray json, JArray redactSettings, JArray salts = null)
        {
            if (redactSettings.Count != json.Count || (!salts.IsNullOrEmpty() && salts.Count != json.Count))
            {
                IDictionary additionalExceptionData = new Dictionary<string, object>
                    {
                        { "errorInObject", redactSettings.Count == json.Count ? "json" : "salts" }
                    };

                throw new BadRequestException("The corresponding JSON or Salt object contains an array that is different in size from the redact settings array. They need to be equally long.", additionalExceptionData);
            }

            // for each element in the array apply the redact function
            for (int i = 0; i < redactSettings.Count; i++)
            {
                if(salts.IsNullOrEmpty())
                {
                    (json[i], _) = RecursiveRedactDataAndSalts(json[i], redactSettings[i]);
                }
                else
                {
                    (json[i], salts[i]) = RecursiveRedactDataAndSalts(json[i], redactSettings[i], salts[i]);
                }
            }

            return (json, salts);
        }

        private (JToken json, JToken salts) RedactJTokenWithCommand(JToken json, JObject command, JToken salts = null)
        {
            if (command.ContainsKey("REDACT:forEach"))
            {
                return RedactArrayForEach((JArray)json, command, salts);
            }

            if (command.ContainsKey("REDACT:ifObjectContains"))
            {
                return RedactIfObjectContains((JObject)json, command, salts);
            }

            // TODO: there are only two DSL commands at the moment. More DSL commands can be added over time

            IDictionary additionalExceptionData = new Dictionary<string, object>
            {
                { "commandObject", command }
            };

            throw new BadRequestException("You tried to use a redact command. The command you used is not valid. Currently available: 'REDACT:forEach'", additionalExceptionData);
        }

        /// <summary>
        /// will apply the redact settings provided for each element of an array
        /// forces the JSON object to by of type array
        /// </summary>
        /// <returns>The array for each.</returns>
        /// <param name="json">Json.</param>
        /// <param name="command">Command.</param>
        /// <param name="salts">Salts.</param>
        private (JToken json, JToken salts) RedactArrayForEach(JArray json, JObject command, JToken salts = null)
        {
            for (int i = 0; i < json.Count; i++)
            {
                if(salts.IsNullOrEmpty())
                {
                    (json[i], _) = RecursiveRedactDataAndSalts(json[i], command["REDACT:forEach"]);
                }
                else
                {
                    (json[i], salts[i]) = RecursiveRedactDataAndSalts(json[i], command["REDACT:forEach"], salts[i]);

                }
            }

            return (json, salts);
        }

        /// <summary>
        /// will apply the redact the object only if the object contains the values provided
        /// </summary>
        /// <returns>The if object contains.</returns>
        /// <param name="json">Json.</param>
        /// <param name="command">Command.</param>
        /// <param name="salts">Salts.</param>
        private (JToken json, JToken salts) RedactIfObjectContains(JObject json, JObject command, JToken salts = null)
        {
            JObject obj = (JObject)command["REDACT:ifObjectContains"];

            foreach ((string key, JToken _) in obj)
            {
                if (!json.ContainsKey(key) || !JToken.DeepEquals(json[key], obj[key]))
                {
                    return (json, salts);
                }
            }

            return RecursiveRedactDataAndSalts(json, true, salts);
        }
    }
}