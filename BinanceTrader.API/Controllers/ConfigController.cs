﻿using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using BinanceTrader.Core;
using Microsoft.AspNetCore.Mvc;

namespace BinanceTrader.API.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ConfigController : Controller
    {
        private const BindingFlags _limitersPropsBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        private readonly CoreConfiguration _config;

        public ConfigController(CoreConfiguration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        [HttpGet]
        public ObjectResult GetConfig()
        {
            return Ok(_config);
        }

        [HttpPost]
        public IActionResult SetLimiters([FromBody][Required]Limiters limiters)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            _config.Limiters = limiters;

            return Ok();
        }
    }
}