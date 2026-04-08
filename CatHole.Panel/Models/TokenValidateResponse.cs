// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CatHole.Panel.Models
{
    public class TokenValidateResponse
    {
        public bool Success { get; set; }
        public string Token { get; set; }
        public string Error { get; set; }
    }
}
