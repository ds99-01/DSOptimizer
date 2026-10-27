using System;
using System.Collections.Generic;
using DSOptimizer.Core.Abstractions;

namespace DSOptimizer.Tests.Unit.Fakes;

public sealed class FakeRestorePointService : IRestorePointService
{
    public RestorePointResult Next { get; set; } = new(RestorePointStatus.Created, null);

    public Exception? ThrowOnCreate { get; set; }

    public List<string> Descriptions { get; } = new();

    public RestorePointResult Create(string description)
    {
        Descriptions.Add(description);
        if (ThrowOnCreate is not null)
        {
            throw ThrowOnCreate;
        }

        return Next;
    }
}
