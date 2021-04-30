module Config

open FSharp.Data

[<Literal>]
let private sampleConfig = "sample_config.json"

type private Settings = JsonProvider<sampleConfig>

type TelegramConfig = Settings.Telegram

let load (path: string) : Async<TelegramConfig> = 
    async {
        let! settings = Settings.AsyncLoad path
        return settings.Telegram
    }
