﻿using Xunit;

namespace Ses.Subscriptions.MsSql.Tests.MsSqlPollerStateRepositoryTests
{
    public class Initializing : TestsBase
    {
        [Fact]
        public async void When_initialize_doesnt_throw()
        {
            await GetEventStore();

            var sut = new MsSqlPollerStateRepository(ConnectionString);

            var x = Record.Exception(() => sut.Initialize());

            Assert.Null(x);
        }

        [Fact]
        public async void When_initialize_and_destroy_doesnt_throw()
        {
            await GetEventStore();

            var sut = new MsSqlPollerStateRepository(ConnectionString);

            var x = Record.Exception(() =>
            {
                sut.Initialize();
                sut.Destroy();
            });

            Assert.Null(x);
        }

        [Fact]
        public async void When_destroy_with_ignoring_errors_doesnt_throw()
        {
            await GetEventStore();

            var sut = new MsSqlPollerStateRepository(ConnectionString);

            var x = Record.Exception(() => sut.Destroy(true));

            Assert.Null(x);
        }

        public Initializing(LocalDbFixture fixture) : base(fixture)
        {

        }
    }
}
