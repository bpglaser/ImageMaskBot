module FileManagement

open System
open System.IO

let saveBytes bytes =
    async {
        let guid = Guid.NewGuid().ToString()
        Directory.CreateDirectory("data") |> ignore
        let path = Path.Join("data", guid)
        do! File.WriteAllBytesAsync(path, bytes) |> Async.AwaitTask
        return guid
    }

let loadFromGuid guid =
    async {
        let path = Path.Join("data", guid)
        try
            let! bytes = File.ReadAllBytesAsync(path) |> Async.AwaitTask
            return Some bytes
        with
        | :? UnauthorizedAccessException
        | :? IOException -> return None
    }

let deleteGuid guid =
    let path = Path.Join("data", guid)
    if File.Exists(path) then File.Delete(path)
