; Simple Hello World for this emulator.
; It writes to serial (FF01/FF02), visible in --headless output.
;
; Build:
;   ./gbasm asm/hello_serial.asm roms/hello_serial.gb
; Run:
;   mono program.exe --headless roms/hello_serial.gb

ORG $0100

start:
    LD HL, message
.loop:
    LD A, (HL)
    OR A
    JR Z, .done
    CALL putc
    INC HL
    JR .loop

.done:
    JP .done

putc:
    LD B, A
.wait:
    LD A, ($FF02)
    OR A
    JR NZ, .wait
    LD A, B
    LD ($FF01), A
    LD A, $81
    LD ($FF02), A
    RET

message:
    DB "HELLO FROM GBASM", 10, 0
