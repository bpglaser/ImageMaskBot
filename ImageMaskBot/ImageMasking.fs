module ImageMasking

open System

open SixLabors.ImageSharp
open SixLabors.ImageSharp.Processing
open SixLabors.ImageSharp.PixelFormats

let writeImageToMemory (image: Image) =
    async {
        use stream = new IO.MemoryStream()
        do! image.SaveAsPngAsync(stream) |> Async.AwaitTask
        let buf: byte array = Array.zeroCreate (stream.Length |> int)
        stream.Seek(0L, IO.SeekOrigin.Begin) |> ignore
        stream.ReadAsync(buf.AsMemory()) |> ignore
        return buf
    }

let maskImage (mask: Image) (image: Image) =
    let options = GraphicsOptions(AlphaCompositionMode = PixelAlphaCompositionMode.SrcIn)
    mask.Clone(fun x -> x.DrawImage(image, options) |> ignore)

let stackImage (bottom: Image) (top: Image) =
    let options = GraphicsOptions(AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver)
    bottom.Clone(fun x -> x.DrawImage(top, options) |> ignore)

let stackImages (images: Image seq) =
    let options = GraphicsOptions(AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver)
    let first = Seq.head images
    images
    |> Seq.tail
    |> Seq.fold (fun (bottom: Image) top -> bottom.Clone(fun x -> x.DrawImage(top, options) |> ignore)) first

let loadImage (path: string): Async<Image<Rgba32>> = Image.LoadAsync(path) |> Async.AwaitTask

let findMaskExtents (image: Image<Rgba32>) =
    seq {
        for x in 0 .. (image.Width - 1) do
            for y in 0 .. (image.Height - 1) do
                yield (x, y, image.Item(x, y))
    }
    |> Seq.filter (fun (_, _, pixel) -> pixel.A <> 0uy)
    |> Seq.fold (fun (minx, miny, maxx, maxy) (x, y, _) ->
        ((if x < minx then x else minx),
         (if y < miny then y else miny),
         (if x > maxx then x else maxx),
         (if y > maxy then y else maxy))) (image.Width, image.Height, 0, 0)

let extentsToDimensions (minx, miny, maxx, maxy) = (maxx - minx, maxy - miny)

let cropToDimensions (width: int, height: int) (image: Image<Rgba32>) =
    image.Clone(fun x -> x.Resize(width, height) |> ignore)

let moveToPosition (width, height) (minx, miny, _, _) (image: Image<Rgba32>) =
    let result = new Image<Rgba32>(Configuration.Default, width, height)
    let options = GraphicsOptions(AlphaCompositionMode = PixelAlphaCompositionMode.SrcOver)
    result.Mutate(fun x -> x.DrawImage(image, Point(minx, miny), options) |> ignore)
    result
