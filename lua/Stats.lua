-- ================================================================
-- MARKET STATS COLLECTOR FOR QUIK (FORTS)
-- ================================================================
--
-- Õ‡ÁÌ‡˜ÂÌËÂ:
--   ¬˚ÒÓÍÓ‰ÂÚ‡ÎËÁËÓ‚‡ÌÌ˚È Ò·Ó ˚ÌÓ˜ÌÓÈ ÒÚ‡ÚËÒÚËÍË Ù¸˛˜ÂÒÓ‚:
--   1. »ÒıÓ‰Ì˚Â Ó·ÂÁÎË˜ÂÌÌ˚Â Ò‰ÂÎÍË (ÚËÍ-‚-ÚËÍ Ò ÒÛ·ÒÂÍÛÌ‰‡ÏË Ë Œ»)
--   2. ÃËÌÛÚÌ˚Â ‡„Â„‡Ú˚ (OHLC, Œ·˙ÂÏ, —‰ÂÎÍË, —Â‰ÌËÈ ˜ÂÍ, Œ·ÓÓÚ,
--      ƒÂÎ¸Ú‡ Œ», CLV, ›ÙÙÂÍÚË‚ÌÓÒÚ¸, œÎÓÚÌÓÒÚ¸ Ó·˙ÂÏ‡ Ì‡ ¯‡„)
--   3. ÃÂÚ‡‰‡ÌÌ˚Â ËÌÒÚÛÏÂÌÚÓ‚ (¯‡„ ˆÂÌ˚, ÒÚÓËÏÓÒÚ¸ ¯‡„‡)
--   4. ÀÓ„ ÒÚ‡·ËÎ¸ÌÓÒÚË Ò‚ˇÁË Ë ‡Á˚‚Ó‚
--   5. œˇÏ‡ˇ ÒËÌıÓÌËÁ‡ˆËˇ Ò SERVERTIME (˜ËÒÚÓÂ ‚ÂÏˇ MSK)
--   6. ¿‚ÚÓÏ‡ÚË˜ÂÒÍÓÂ ÒÓÁ‰‡ÌËÂ Í‡Ú‡ÎÓ„Ó‚ ·ÂÁ ÏÂˆ‡ÌËˇ ÓÍÓÌ
--
-- ================================================================

local CLASS_CODE = "SPBFUT"

-- Õ‡ÒÚÓÈÍË ˆËÍÎÓ‚
local LOOP_INTERVAL      = 500   -- ŒÔÓÒ ÌÓ‚˚ı Ò‰ÂÎÓÍ, ÏÒ
local FLUSH_INTERVAL     = 5     -- —·ÓÒ ·ÛÙÂÓ‚ Ì‡ ‰ËÒÍ, ÒÂÍ
local BAR_CLOSE_DELAY    = 10    -- «‡‰ÂÊÍ‡ Á‡Í˚ÚËˇ ·‡‡, ÒÂÍ

-- ‘Î‡„Ë ÒÓı‡ÌÂÌËˇ
local SAVE_RAW_TRADES      = true
local SAVE_MINUTE_BARS     = true
local SAVE_INSTRUMENT_INFO = true

-- ¡‡ÁÓ‚‡ˇ ‰ËÂÍÚÓËˇ
local ROOT_DIR = getScriptPath() .. "\\MarketStats"

-- ================================================================
-- √–”œœ€ »Õ—“–”Ã≈Õ“Œ¬
-- ================================================================

local FUTURES_GROUPS = {
    "BR", "GD", "Si", "SF", "CR", "EH", "CC", "SV"
}

local PERPETUAL_FUTURES = {
    "CNYRUBF", "IMOEXF", "USDRUBF"
}

local MONTH_CODES = "FGHJKMNQUVXZ"

-- ================================================================
-- —Œ—“ŒﬂÕ»≈
-- ================================================================

local is_run = true
local t_id   = nil

local instruments = {}
local last_trade_index = 0
local last_flush_time = 0

--  ˝¯ ÒÂ‚ÂÌÓ„Ó ‚ÂÏÂÌË
local g_last_server_ts = 0
local g_last_local_ts  = os.time()

local connected = false
local connection_gaps = {}
local connection_lost_at = nil
local last_connection_check = nil

local session_id = os.date("%Y%m%d_%H%M%S")
local session_dir = ROOT_DIR .. "\\Sessions\\" .. session_id
local trades_dir  = session_dir .. "\\Trades"
local minutes_dir = session_dir .. "\\Minutes"
local logs_dir    = session_dir .. "\\Logs"
local info_dir    = session_dir .. "\\Instruments"

local log_file        = nil
local connection_file = nil
local info_file       = nil

local total_processed_trades = 0
local total_written_bars   = 0
local last_processed_time  = "-"

-- ================================================================
-- UTILS
-- ================================================================

local function SafeNumber(x)
    if x == nil then return nil end
    if type(x) == "string" then
        x = x:gsub("%s+", ""):gsub(",", ".")
    end
    return tonumber(x)
end

local function FormatNumber(x)
    if x == nil then return "" end
    local s = string.format("%.8f", x)
    s = s:gsub("0+$", ""):gsub("%.$", "")
    return s
end

local function GetMinute(ts)
    return math.floor(ts / 60) * 60
end

local function DateTimeToTimestamp(dt)
    if not dt or not dt.year or dt.year == 0 then return nil end
    return os.time({
        year  = dt.year,
        month = dt.month,
        day   = dt.day,
        hour  = dt.hour,
        min   = dt.min,
        sec   = dt.sec
    })
end

-- ================================================================
-- “Œ◊ÕŒ≈ ¡»–∆≈¬Œ≈ ¬–≈Ãﬂ (—≈–¬≈– MSK)
-- ================================================================

local function GetServerTime()
    local st = getInfoParam("SERVERTIME")
    local td = getInfoParam("TRADEDATE")
    local now_local = os.time()

    if st and st ~= "" and td and td ~= "" then
        local d, m, y = td:match("(%d+)%.(%d+)%.(%d+)")
        local hr, min, sec = st:match("(%d+):(%d+):(%d+)")
        if d and hr then
            local ts = os.time({
                year  = tonumber(y),
                month = tonumber(m),
                day   = tonumber(d),
                hour  = tonumber(hr),
                min   = tonumber(min),
                sec   = tonumber(sec)
            })
            g_last_server_ts = ts
            g_last_local_ts  = now_local
            return ts
        end
    end

    -- ≈ÒÎË ÔË ‡Á˚‚Â SERVERTIME ÔÛÒÚ, ‡ÍÍÛ‡ÚÌÓ ¯‡„‡ÂÏ ÔÓ ÎÓÍ‡Î¸ÌÓÏÛ Ú‡ÈÏÂÛ
    if g_last_server_ts > 0 then
        return g_last_server_ts + (now_local - g_last_local_ts)
    end

    return now_local
end

local function DateString(ts)     return os.date("%Y-%m-%d", ts) end
local function TimeString(ts)     return os.date("%H:%M:%S", ts) end
local function DateTimeString(ts) return os.date("%Y-%m-%d %H:%M:%S", ts) end

local function GetTradeSubsecond(dt)
    if not dt then return "" end
    if dt.mcs ~= nil then return tostring(dt.mcs) end
    if dt.ms ~= nil  then return tostring(dt.ms) end
    return ""
end

-- ================================================================
-- CSV ‘”Õ ÷»»
-- ================================================================

local function CsvEscape(value)
    if value == nil then return "" end
    local s = tostring(value)
    if s:find(";", 1, true) or s:find('"', 1, true) or s:find("\n", 1, true) then
        s = '"' .. s:gsub('"', '""') .. '"'
    end
    return s
end

local function CsvRow(values)
    local res = {}
    for i = 1, #values do
        res[i] = CsvEscape(values[i])
    end
    return table.concat(res, ";")
end

local function WriteLine(file, text)
    if not file then return false end
    return file:write(text .. "\n")
end

-- ================================================================
-- «¿√ŒÀŒ¬ » CSV
-- ================================================================

local RAW_HEADER = table.concat({
    "SessionId", "TradeNum", "Date", "Time", "Subsecond",
    "ClassCode", "SecCode", "GroupCode", "Price", "Qty",
    "Value", "Flags", "OpenInterestRaw", "ReceivedAt"
}, ";")

local MINUTE_HEADER = table.concat({
    "SessionId", "Date", "Time", "ClassCode", "SecCode", "GroupCode",
    "Open", "High", "Low", "Close", "Volume", "TradesCount", "AvgTradeSize",
    "Turnover", "OpenInterestFirst", "OpenInterestLast", "OpenInterestChange",
    "PriceChange", "PriceChangePct", "PriceRange", "CLV", "Efficiency",
    "VolumePerPriceStep", "FirstTradeTime", "LastTradeTime", "DataStatus"
}, ";")

local INFO_HEADER = table.concat({
    "SessionId", "DetectedAt", "ClassCode", "SecCode", "GroupCode",
    "IsPerpetual", "Name", "ShortName", "MatDate", "Scale", "MinPriceStep", "StepPrice"
}, ";")

-- ================================================================
-- »ƒ≈Õ“»‘» ¿÷»ﬂ »Õ—“–”Ã≈Õ“Œ¬
-- ================================================================

local function IsPerpetual(sec_code)
    for _, sec in ipairs(PERPETUAL_FUTURES) do
        if sec_code == sec then return true end
    end
    return false
end

local function GetFuturesGroup(sec_code)
    if IsPerpetual(sec_code) then
        return sec_code, true
    end

    for _, group in ipairs(FUTURES_GROUPS) do
        local prefix = "^" .. group .. "([" .. MONTH_CODES .. "])" .. "(%d)$"
        if sec_code:match(prefix) then
            return group, false
        end
    end
    return nil, false
end

local function GetParamNumber(sec_code, param_name)
    local p = getParamEx(CLASS_CODE, sec_code, param_name)
    if not p then return nil end
    return SafeNumber(p.param_value)
end

-- ================================================================
-- ‘¿…ÀŒ¬¿ﬂ —»—“≈Ã¿
-- ================================================================

local function InitDirectories()
    local cmd = string.format('cmd /c mkdir "%s" "%s" "%s" "%s" >nul 2>nul',
        trades_dir,
        minutes_dir,
        logs_dir,
        info_dir
    )
    os.execute(cmd)
end

local function OpenFile(path)
    return io.open(path, "a")
end

-- ================================================================
-- »Õ»÷»¿À»«¿÷»ﬂ »Õ—“–”Ã≈Õ“¿
-- ================================================================

local function GetOrCreateInstrument(sec_code)
    if instruments[sec_code] then
        return instruments[sec_code]
    end

    local group, perpetual = GetFuturesGroup(sec_code)
    if not group then return nil end

    local raw_file = nil
    local minute_file = nil

    if SAVE_RAW_TRADES then
        local path = trades_dir .. "\\" .. sec_code .. ".csv"
        local is_new = not io.open(path, "r")
        raw_file = OpenFile(path)
        if raw_file and is_new then
            WriteLine(raw_file, RAW_HEADER)
        end
    end

    if SAVE_MINUTE_BARS then
        local path = minutes_dir .. "\\" .. sec_code .. ".csv"
        local is_new = not io.open(path, "r")
        minute_file = OpenFile(path)
        if minute_file and is_new then
            WriteLine(minute_file, MINUTE_HEADER)
        end
    end

    local sec_info = getSecurityInfo(CLASS_CODE, sec_code) or {}

    local instrument = {
        sec_code    = sec_code,
        group       = group,
        perpetual   = perpetual,
        raw_file    = raw_file,
        minute_file = minute_file,
        bars        = {},
        price_step  = GetParamNumber(sec_code, "SEC_PRICE_STEP") or 1,
        step_price  = GetParamNumber(sec_code, "STEPPRICE") or 1
    }

    instruments[sec_code] = instrument

    if SAVE_INSTRUMENT_INFO and info_file then
        local row = {
            session_id,
            DateTimeString(GetServerTime()),
            CLASS_CODE,
            sec_code,
            group,
            perpetual and "1" or "0",
            sec_info.name or "",
            sec_info.short_name or "",
            sec_info.mat_date or "",
            sec_info.scale or "",
            FormatNumber(instrument.price_step),
            FormatNumber(instrument.step_price)
        }
        WriteLine(info_file, CsvRow(row))
        info_file:flush()
    end

    return instrument
end

-- ================================================================
-- ÀŒ√»–Œ¬¿Õ»≈ » —Œ≈ƒ»Õ≈Õ»≈
-- ================================================================

local function Log(text)
    if log_file then
        WriteLine(log_file, CsvRow({ DateTimeString(GetServerTime()), text }))
        log_file:flush()
    end
end

local function WriteConnectionEvent(event_name, timestamp, details)
    if connection_file then
        WriteLine(connection_file, CsvRow({ DateTimeString(timestamp), event_name, details or "" }))
        connection_file:flush()
    end
end

local function UpdateConnection()
    local now = GetServerTime()
    local new_connected = (isConnected() == 1)

    if last_connection_check == nil then
        last_connection_check = now
        connected = new_connected
        WriteConnectionEvent(connected and "CONNECTED" or "DISCONNECTED", now, "Start")
        return
    end

    if new_connected ~= connected then
        if not new_connected then
            connection_lost_at = last_connection_check
            WriteConnectionEvent("DISCONNECTED", now, "")
            Log("Connection lost")
        else
            if connection_lost_at then
                table.insert(connection_gaps, { start_ts = connection_lost_at, end_ts = now })
            end
            connection_lost_at = nil
            WriteConnectionEvent("CONNECTED", now, "")
            Log("Connection restored")
        end
    end

    connected = new_connected
    last_connection_check = now
end

local function IntervalHasGap(start_ts, end_ts)
    for _, gap in ipairs(connection_gaps) do
        if gap.start_ts < end_ts and gap.end_ts > start_ts then
            return true
        end
    end
    if connection_lost_at and connection_lost_at < end_ts then
        return true
    end
    return false
end

-- ================================================================
-- —“–” “”–¿ Ã»Õ”“ÕŒ√Œ ¡¿–¿
-- ================================================================

local function CreateBar(minute)
    return {
        minute = minute,
        open = nil, high = nil, low = nil, close = nil,
        open_key = nil, close_key = nil,
        volume = 0, trades_count = 0,
        turnover = 0, turnover_available = true,
        oi_first = nil, oi_last = nil,
        oi_first_key = nil, oi_last_key = nil,
        first_trade_ts = nil, last_trade_ts = nil
    }
end

local function KeyLess(a, b)
    if not a then return true end
    if a.ts ~= b.ts then return a.ts < b.ts end
    if a.sub ~= b.sub then return a.sub < b.sub end
    return a.num < b.num
end

-- ================================================================
-- Œ¡–¿¡Œ“ ¿ “» ¿ (—ƒ≈À »)
-- ================================================================

local function ProcessTrade(tr)
    if not tr or tr.class_code ~= CLASS_CODE or not tr.sec_code then return end

    local instrument = GetOrCreateInstrument(tr.sec_code)
    if not instrument then return end

    local price = SafeNumber(tr.price)
    local qty   = SafeNumber(tr.qty)
    if not price or not qty or price <= 0 or qty <= 0 then return end

    local trade_ts = DateTimeToTimestamp(tr.datetime)
    if not trade_ts then return end

    local minute    = GetMinute(trade_ts)
    local trade_num = tr.trade_num or ""
    local flags     = tr.flags or ""
    local value     = SafeNumber(tr.value)
    local oi        = SafeNumber(tr.open_interest)

    -- 1. «‡ÔËÒ¸ RAW Ò‰ÂÎÍË
    if instrument.raw_file then
        local raw_row = {
            session_id, tostring(trade_num), DateString(trade_ts), TimeString(trade_ts),
            GetTradeSubsecond(tr.datetime), CLASS_CODE, tr.sec_code, instrument.group,
            FormatNumber(price), FormatNumber(qty), FormatNumber(value), tostring(flags),
            FormatNumber(oi), DateTimeString(os.time())
        }
        WriteLine(instrument.raw_file, CsvRow(raw_row))
    end

    total_processed_trades = total_processed_trades + 1
    last_processed_time = TimeString(trade_ts)

    -- 2. ÃËÌÛÚÌ˚È ·‡ (Ò‡‚ÌÂÌËÂ ÒÚÓ„Ó Ò GetServerTime() ÔÓ MSK!)
    if not SAVE_MINUTE_BARS then return end
    if (minute + 60 + BAR_CLOSE_DELAY) <= GetServerTime() then return end

    local bar = instrument.bars[minute]
    if not bar then
        bar = CreateBar(minute)
        instrument.bars[minute] = bar
    end

    local key = {
        ts  = trade_ts,
        sub = SafeNumber(GetTradeSubsecond(tr.datetime)) or 0,
        num = SafeNumber(trade_num) or 0
    }

    if not bar.open_key or KeyLess(key, bar.open_key) then
        bar.open = price
        bar.open_key = key
        bar.first_trade_ts = trade_ts
    end

    if not bar.close_key or KeyLess(bar.close_key, key) then
        bar.close = price
        bar.close_key = key
        bar.last_trade_ts = trade_ts
    end

    if not bar.high or price > bar.high then bar.high = price end
    if not bar.low  or price < bar.low  then bar.low  = price end

    bar.volume = bar.volume + qty
    bar.trades_count = bar.trades_count + 1

    if value ~= nil then
        bar.turnover = bar.turnover + value
    else
        bar.turnover_available = false
    end

    if oi ~= nil then
        if not bar.oi_first_key or KeyLess(key, bar.oi_first_key) then
            bar.oi_first = oi
            bar.oi_first_key = key
        end
        if not bar.oi_last_key or KeyLess(bar.oi_last_key, key) then
            bar.oi_last = oi
            bar.oi_last_key = key
        end
    end
end

-- ================================================================
-- «¿œ»—‹ «¿ –€“Œ√Œ Ã»Õ”“ÕŒ√Œ ¡¿–¿
-- ================================================================

local function WriteMinuteBar(instrument, bar)
    if not instrument.minute_file then return end

    local minute = bar.minute
    local avg_trade_size = (bar.trades_count > 0) and (bar.volume / bar.trades_count) or nil

    local price_change = (bar.open and bar.close) and (bar.close - bar.open) or nil
    local price_change_pct = (price_change and bar.open and bar.open ~= 0) and (price_change / bar.open * 100) or nil
    local price_range = (bar.high and bar.low) and (bar.high - bar.low) or nil

    local clv = nil
    local efficiency = nil
    local volume_per_step = nil

    if price_range and price_range > 0 then
        clv = (2 * bar.close - bar.high - bar.low) / price_range
        efficiency = math.abs(price_change or 0) / price_range

        if instrument.price_step > 0 then
            local steps = price_range / instrument.price_step
            if steps > 0 then
                volume_per_step = bar.volume / steps
            end
        end
    end

    local oi_change = (bar.oi_first and bar.oi_last) and (bar.oi_last - bar.oi_first) or nil
    local turnover = bar.turnover_available and bar.turnover or nil

    local first_trade_time = bar.first_trade_ts and TimeString(bar.first_trade_ts) or ""
    local last_trade_time  = bar.last_trade_ts  and TimeString(bar.last_trade_ts)  or ""
    local status = IntervalHasGap(minute, minute + 60) and "GAP" or "OK"

    local row = {
        session_id, DateString(minute), TimeString(minute), CLASS_CODE,
        instrument.sec_code, instrument.group,
        FormatNumber(bar.open), FormatNumber(bar.high), FormatNumber(bar.low), FormatNumber(bar.close),
        FormatNumber(bar.volume), tostring(bar.trades_count), FormatNumber(avg_trade_size),
        FormatNumber(turnover), FormatNumber(bar.oi_first), FormatNumber(bar.oi_last),
        FormatNumber(oi_change), FormatNumber(price_change), FormatNumber(price_change_pct),
        FormatNumber(price_range), FormatNumber(clv), FormatNumber(efficiency),
        FormatNumber(volume_per_step), first_trade_time, last_trade_time, status
    }

    WriteLine(instrument.minute_file, CsvRow(row))
    total_written_bars = total_written_bars + 1
end

local function FlushCompletedBars()
    local now = GetServerTime()
    for _, instrument in pairs(instruments) do
        local completed = {}
        for minute, _ in pairs(instrument.bars) do
            if (minute + 60 + BAR_CLOSE_DELAY) <= now then
                table.insert(completed, minute)
            end
        end

        table.sort(completed)
        for _, minute in ipairs(completed) do
            local bar = instrument.bars[minute]
            if bar then
                WriteMinuteBar(instrument, bar)
                instrument.bars[minute] = nil
            end
        end
    end
end

-- ================================================================
-- —¡–Œ— » «¿ –€“»≈ ‘¿…ÀŒ¬
-- ================================================================

local function FlushFiles()
    for _, inst in pairs(instruments) do
        if inst.raw_file then inst.raw_file:flush() end
        if inst.minute_file then inst.minute_file:flush() end
    end
    if info_file then info_file:flush() end
    if connection_file then connection_file:flush() end
    if log_file then log_file:flush() end
end

local function CloseFiles()
    FlushFiles()
    for _, inst in pairs(instruments) do
        if inst.raw_file then inst.raw_file:close(); inst.raw_file = nil end
        if inst.minute_file then inst.minute_file:close(); inst.minute_file = nil end
    end
    if info_file then info_file:close(); info_file = nil end
    if connection_file then connection_file:close(); connection_file = nil end
    if log_file then log_file:close(); log_file = nil end
end

-- ================================================================
-- GUI Œ ÕŒ —“¿“”—¿ ¬ QUIK
-- ================================================================

local function InitStatusTable()
    t_id = AllocTable()
    AddColumn(t_id, 1, "œ‡‡ÏÂÚ", true, QTABLE_STRING_TYPE, 24)
    AddColumn(t_id, 2, "«Ì‡˜ÂÌËÂ", true, QTABLE_STRING_TYPE, 30)
    CreateWindow(t_id)
    SetWindowCaption(t_id, "Market Stats Collector")
    SetWindowPos(t_id, 80, 80, 420, 200)

    for i = 1, 6 do
        InsertRow(t_id, -1)
    end
end

local function UpdateStatusTable()
    if not t_id then return end

    local active_count = 0
    for _ in pairs(instruments) do active_count = active_count + 1 end

    local s_time = TimeString(GetServerTime())

    SetCell(t_id, 1, 1, "—ÂÒÒËˇ")
    SetCell(t_id, 1, 2, session_id)

    SetCell(t_id, 2, 1, "—ÓÂ‰ËÌÂÌËÂ")
    SetCell(t_id, 2, 2, (connected) and "¬ —≈“»" or "Œ¡–€¬ —¬ﬂ«»")

    SetCell(t_id, 3, 1, "¬ÂÏˇ ·ËÊË (MSK)")
    SetCell(t_id, 3, 2, s_time)

    SetCell(t_id, 4, 1, "¿ÍÚË‚Ì˚ı ËÌÒÚÛÏÂÌÚÓ‚")
    SetCell(t_id, 4, 2, tostring(active_count))

    SetCell(t_id, 5, 1, "Œ·‡·ÓÚ‡ÌÓ ÚËÍÓ‚")
    SetCell(t_id, 5, 2, string.format("%d (ÔÓÒÎ: %s)", total_processed_trades, last_processed_time))

    SetCell(t_id, 6, 1, "«‡ÔËÒ‡ÌÓ 1m ·‡Ó‚")
    SetCell(t_id, 6, 2, tostring(total_written_bars))
end

-- ================================================================
-- Œ¡–¿¡Œ“ ¿ ÕŒ¬€’ —ƒ≈ÀŒ 
-- ================================================================

local function ProcessNewTrades()
    local n = getNumberOf("all_trades")
    if not n or n <= 0 then return end

    if n < last_trade_index then
        Log("WARNING: all_trades reset detected")
        WriteConnectionEvent("TABLE_RESET", GetServerTime(), "index decreased")
        last_trade_index = 0
    end

    for i = last_trade_index, n - 1 do
        local tr = getItem("all_trades", i)
        if tr then
            ProcessTrade(tr)
        end
    end

    last_trade_index = n
end

-- ================================================================
-- MAIN
-- ================================================================

function main()
    InitDirectories()

    log_file = io.open(logs_dir .. "\\events.csv", "a")
    if log_file then WriteLine(log_file, "DateTime;Event") end

    connection_file = io.open(logs_dir .. "\\connections.csv", "a")
    if connection_file then WriteLine(connection_file, "DateTime;Event;Details") end

    if SAVE_INSTRUMENT_INFO then
        info_file = io.open(info_dir .. "\\instruments.csv", "a")
        if info_file then WriteLine(info_file, INFO_HEADER) end
    end

    InitStatusTable()
    UpdateConnection()

    last_trade_index = getNumberOf("all_trades") or 0
    last_flush_time = os.time()

    Log("MarketStats started. Session: " .. session_id)

    while is_run do
        if IsWindowClosed(t_id) then
            is_run = false
            break
        end

        UpdateConnection()

        if connected then
            ProcessNewTrades()
        end

        FlushCompletedBars()

        local now = os.time()
        if (now - last_flush_time) >= FLUSH_INTERVAL then
            FlushFiles()
            last_flush_time = now
        end

        UpdateStatusTable()
        sleep(LOOP_INTERVAL)
    end

    Log(string.format("Stopped. Trades: %d, Bars: %d", total_processed_trades, total_written_bars))
    CloseFiles()
end

-- ================================================================
-- STOP
-- ================================================================

function OnStop()
    is_run = false
    if t_id then
        DestroyTable(t_id)
        t_id = nil
    end
    return 1000
end