﻿using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json.Linq;

namespace ObjectHashServer.Models.API.Request
{
    public class ObjectHashRequestModel
    {
        [Required]
        public JToken Object { get; set; }
        // optional
        public JToken Salts { get; set; }
    }
}
