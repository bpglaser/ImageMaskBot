module FunogramHelpers

open System.Net.Http

open Funogram.Api
open Funogram.Telegram.Api
open Funogram.Telegram.Bot
open Funogram.Telegram.Types

type FunogramUser =
    { Id: int64
      Username: string }

let private downloadFile token (client: HttpClient) (file: File) =
    file.FilePath
    |> Option.map (sprintf "https://api.telegram.org/file/bot%s/%s" token)
    |> Option.map (fun address ->
        client.GetByteArrayAsync(address) |> Async.AwaitTask)
    |> Option.defaultWith (fun () -> async { return [||] })

let getPhotoBytes botConfig client (photo: PhotoSize) =
    async {
        let! result = photo.FileId
                      |> getFile
                      |> api botConfig
        match result with
        | Ok file ->
            let! result = downloadFile botConfig.Token client file
            return Ok result
        | Error err -> return Error err
    }

let getUser (update: UpdateContext) =
    match update.Update.Message with
    | None -> None
    | Some message ->
        let id = message.Chat.Id
        match message.Chat.Username with
        | None -> None
        | Some username ->
            Some
                { Id = id
                  Username = username }
