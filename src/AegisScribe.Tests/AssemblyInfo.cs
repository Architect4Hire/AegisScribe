// Every integration-test collection here boots a real AppHost whose api/gateway/web processes bind
// launchSettings.json's FIXED ports (chosen so `aspire run` gives a human predictable URLs). xUnit
// runs distinct [Collection]s in parallel by default, and two fixture instances racing for the same
// fixed port produce exactly the kind of cross-test interference a fixed port can't survive — one
// process silently loses the bind and traffic ends up crossing between unrelated tests. Collections
// must run one at a time until these tests stop depending on fixed ports.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
