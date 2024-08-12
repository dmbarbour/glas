module RandVal

    open Glas
    open Value

    let valDepth = 8

    // global rng is fine for testing
    let rng = System.Random()

    let randomBytes ct =
        let arr = Array.zeroCreate ct
        rng.NextBytes(arr)
        arr

    let randomRange m n = 
        (min m n) + (rng.Next() % (1 + (abs (m - n)))) 

    let labels = Array.ofList [
        // grabbed from randomwordgenerator.com
        "banana"; "market"; "entry"; "empire"; "feast"; "watch";
        "ride"; "quote"; "article"; "snake"; "flatware"; "particle";
        "humanity"; "means"; "sunrise"; "construct"; "burn"; "branch";
        "instrument"; "candle"; "fear"; "introduction"; "beautiful"; "record";
        "boot"; "subject"; "embrace"; "roar"; "designer"; "give";
        "man"; "hope"; "applied"; "jurisdiction"; "tender"; "variable";
        "snatch"; "final"; "dialogue"; "trait"; "echo"; "presence";
        "ticket"; "stake"; "mountain"; "hide"; "steep"; "beef";
        "soldier"; "sympathetic"; "salon"; "operational"; "overwhelm"; "mosque";
        "treatment"; "rubbish"; "reservoir"; "appeal"; "eye"; "war";
        "monkey"; "classify"; "expansion"; "outside"; "food"; "training";
        "freedom"; "seal"; "outfit"; "banish"; "pioneer"; "chalk";
        "treatment"; "reception"; "voice"; "recording"; "veteran"; "different";
        "patient"; "swim"; "snub"; "professor"; "lease"; "digress";
        "rotation"; "color-blind"; "cabin"; "depression"; "even"; "regulation";
        "adjust"; "dorm"; "counter"; "free"; "improvement"; "accept";
        "audience"; "determine"; "solution"; "organization"; "celebration"; "order";
    ]

    let randomLabel () =
        let ix = rng.Next() % labels.Length
        labels[ix]

    let mkRandomInt () =
        let buffer = Array.zeroCreate 8
        rng.NextBytes(buffer)
        System.BitConverter.ToInt64(buffer)

    let mkRandomIntVal () =
        Value.ofInt (mkRandomInt())

    let rec mkRandomVal (d : int) =
        let sel = rng.Next () % 100
        if ((sel < 20) || (d < 1)) then
            mkRandomIntVal()     
        elif (sel < 30) then
            Value.unit
        elif (sel < 40) then
            let a = mkRandomVal (d - 1)
            let b = mkRandomVal (d - 1)
            Value.pair a b
        elif (sel < 45) then 
            Value.left (mkRandomVal (d - 1))
        elif (sel < 50) then
            Value.right (mkRandomVal (d - 1))
        elif (sel < 60) then 
            Value.variant (randomLabel()) (mkRandomVal (d - 1))
        elif (sel < 80) then
            Value.ofTerm (mkRandomRope (d - 1))
        else
            mkRandomRecord (d - 1) (randomRange 3 9)

    and mkRandomRope (d : int) : Term =
        let sel = rng.Next() % 40
        if((sel < 10) || (d < 1)) then
            Binary(ImmArray.UnsafeOfArray(randomBytes (randomRange 12 24)))
        elif(sel < 20) then
            let arr = Array.init (randomRange 6 12) (fun _ -> mkRandomVal (d - 1))
            Array(ImmArray.UnsafeOfArray(arr)) 
        else
            let ct = randomRange 3 9
            let lFrags = List.init ct (fun _ -> mkRandomRope (d - 1))
            List.fold (Rope.append) Leaf lFrags

    and mkRandomRecord d ct =
        let ks = List.init ct (fun _ -> randomLabel ())
        let vs = List.init ct (fun _ -> mkRandomVal d)
        Value.asRecord ks vs



