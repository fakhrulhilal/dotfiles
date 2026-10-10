# Background scheduler jobs for .NET Native AOT

As of 2026-10-10. Everything here is from memory and was not checked against current releases or documentation. Confirm
the AOT column with a real build before relying on it.

## Conclusion

No library meets all four requirements cleanly. The ones with mature PostgreSQL and SQLite storage (Hangfire,
Quartz.NET) rely on reflection, and the ones built for Native AOT have weak or no persistence.

The four requirements:

1. Native AOT compatibility
2. Recurring schedules plus queued, fire-and-forget jobs
3. One-time jobs at a specific time
4. Persistent storage on at least PostgreSQL and SQLite

## Comparison

| Library             | Native AOT                                                            | Recurring and fire-and-forget                                                     | One-time at a specific time              | PostgreSQL and SQLite                    |
|---------------------|-----------------------------------------------------------------------|-----------------------------------------------------------------------------------|------------------------------------------|------------------------------------------|
| Hangfire (baseline) | No: jobs are serialized expression trees, using Newtonsoft.Json       | Both, with real queues, retries and continuations                                 | Yes (`Schedule` with a `DateTimeOffset`) | Both, through community storage packages |
| Quartz.NET          | Not supported in 3.x: job types are loaded by name through reflection | Cron triggers yes; no queue concept, fire-and-forget is a trigger that starts now | Yes (simple trigger with `StartAt`)      | Both, built into its ADO job store       |
| TickerQ             | Designed for it: job functions are discovered by a source generator   | Cron jobs yes; fire-and-forget is a time job due now, with retries                | Yes (time jobs)                          | Both, but only through EF Core           |
| NCronJob            | Unverified                                                            | Cron jobs and instant jobs                                                        | Yes (run at a time or after a delay)     | None, in-memory only                     |
| Coravel             | Unverified                                                            | Fluent schedules and an in-memory queue                                           | No built-in equivalent                   | None in the free version                 |

## Notes per library

- **TickerQ** is the closest to Hangfire in shape, and it has a dashboard. Its persistence goes through EF Core, whose
  Native AOT support was still experimental as far as I know. The scheduler core may be AOT-clean while the storage
  layer brings the IL warnings back.
- **Quartz.NET** meets requirements 2 to 4 best but fails on AOT. A 4.x release was in progress with reduced reflection;
  whether it reached AOT compatibility is unknown.
- **NCronJob and Coravel** are out, because jobs are lost on restart.
- **Hangfire** is listed only as the baseline. Its job serialization model cannot work under Native AOT.

## Recommendation

Try TickerQ first, and fall back to a small custom job store if its storage layer is not AOT-clean.

1. Write a throwaway file-based app that uses TickerQ with its EF Core storage on SQLite.
2. Run `dotnet build` and count the IL2xxx and IL3xxx warnings. Zero is the bar.
3. If it is not clean, build a job store on the existing `PostgreHelper` and `SqliteHelper`, with Cronos for the cron
   calculations, rather than fight Quartz.NET.

The custom store is small: one jobs table with a due time, a status and a JSON payload covers recurring, fire-and-forget
and one-time jobs. Its costs:

- Retries, crash recovery and any dashboard are yours to write and maintain.
- SQLite has no row-level locking, so it is limited to a single worker process. PostgreSQL can run several workers with
  `FOR UPDATE SKIP LOCKED`.

## Open questions

- [ ] Does TickerQ with EF Core storage build with zero trim and AOT warnings?
- [ ] Did Quartz.NET 4.x ship with Native AOT support?
- [ ] Is a dashboard required, or is a CLI view of the jobs table enough?