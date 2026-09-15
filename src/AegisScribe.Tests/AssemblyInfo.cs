// Integration-test collections boot a real AppHost whose processes bind launchSettings.json's FIXED
// ports. xUnit runs distinct [Collection]s in parallel by default, and two fixtures racing for the same
// port means one silently loses the bind and traffic crosses between unrelated tests. Collections must
// run one at a time until these tests stop depending on fixed ports.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
