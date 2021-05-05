[<EntryPoint>]
let main argv =
    let db = UserState.init()
    try
        async {
            let! config = argv.[0] |> Config.load
            do! ImageMaskBot.run db config } |> Async.RunSynchronously
    finally
        UserState.cleanup db
    0
