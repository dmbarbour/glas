namespace Glas

module Stats = 

    type S =
        {
            Cnt : uint64
            Sum : float
            SSQ : float
            Min : float
            Max : float
        }

    let inline private sq x = (x * x)

    let s1 f = 
        { Cnt = 1UL
        ; Sum = f
        ; SSQ = sq f 
        ; Min = f
        ; Max = f
        }

    let join a b = 
        { Cnt = (a.Cnt + b.Cnt)
        ; Sum = (a.Sum + b.Sum)
        ; SSQ = (a.SSQ + b.SSQ)
        ; Min = min (a.Min) (b.Min)
        ; Max = max (a.Max) (b.Max)
        }

    let average s =
        assert(s.Cnt > 0UL)
        (s.Sum / float(s.Cnt))

    let variability s =
        // (SSQ - N*(AVG^2)) / (N - 1)
        // N*(Avg^2) = N*((Sum/N)^2) = N*Sum^2 / N^2 = Sum^2 / N 
        // (SSQ - (Sum^2/N)) / (N - 1)
        // changing denominator to N (not exact variability)
        assert(s.Cnt > 0UL)
        let recipN = 1.0 / float(s.Cnt)
        (s.SSQ - ((sq s.Sum) * recipN)) * recipN

    let sdev s = 
        sqrt (variability s)

    let private heuristicJoinCost s1 s2 =
        // a weighted distance between boxes and their joined average.
        // This also weighs merging more points more heavily than fewer.
        let joinAvg = (s1.Sum + s2.Sum) / float(s1.Cnt + s2.Cnt)
        float(s1.Cnt) * sq(joinAvg - (average s1)) +
        float(s2.Cnt) * sq(joinAvg - (average s2))

    // will reduce every sequence of 5 items to 4 items.
    let rec private reduceFifths acc rs =
        match rs with 
        | (r1::r2::r3::r4::r5::rs') -> // 5 or more items, reduce to 4 items
            let c12 = heuristicJoinCost r1 r2
            let c23 = heuristicJoinCost r2 r3
            let c34 = heuristicJoinCost r3 r4
            let c45 = heuristicJoinCost r4 r5
            let cMin = min (min c12 c23) (min c34 c45)
            let acc' =
                if (cMin = c12) then (r5::r4::r3::(join r2 r1)::acc) else
                if (cMin = c23) then (r5::r4::(join r3 r2)::r1::acc) else
                if (cMin = c34) then (r5::(join r4 r3)::r2::r1::acc) else
                assert(cMin = c45);  ((join r5 r4)::r3::r2::r1::acc)
            reduceFifths acc' rs'
        | (r::rs') -> // fewer than 5 items, is not reduced
            reduceFifths (r::acc) rs'
        | [] -> acc // preserve reverse order.

    // heuristic exponential reduction of stats buckets.
    // This reduces ~36% of items (9 per 25).
    let reduce l =
        l |> List.filter (fun s -> (s.Cnt > 0UL))
          // sort so items with similar averages are merged.
          |> List.sortBy average
          // use two 5=>4 reduction passes to avoid aliasing.
          |> reduceFifths []
          |> reduceFifths []

    // reduce to a specific number of buckets
    let rec reduceTo n l =
        if (List.length l > n) 
          then reduceTo n (reduce l)
          else l

    [<Struct>]
    type MultiStat = 
        { BCT : int
        ; BS  : S list 
        }

    let autoReduce ms =
        if (100 > ms.BCT) then ms else
        let bs' = reduce ms.BS
        { BS = bs'
        ; BCT = List.length bs'
        }

    let addVal ms v =
        autoReduce { BCT = 1 + (ms.BCT); BS = (s1 v)::(ms.BS)}


