// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace CatHole.Core
{
    public class RelayOption
    {
        public string Name { get; set; } = "Unnamed";

        public string ListenHost { get; set; } = "127.0.0.1:45678";

        public string TargetHost { get; set; } = "127.0.0.1:45678";

        public int BufferSize { get; set; } = 128 * 1024;

        public bool TCP { get; set; } = true;

        public bool UDP { get; set; } = true;

        public int Timeout { get; set; } = 1000; // miliseconds
    }
}
