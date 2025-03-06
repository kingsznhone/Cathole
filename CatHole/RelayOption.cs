// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;

namespace CatHole
{
    public class RelayOption
    {
        public string ExternalHost;
        public string InternalHost;
        public int BufferSize;
        public bool TCP;
        public bool UDP;
        public int Timeout;
    }
}
