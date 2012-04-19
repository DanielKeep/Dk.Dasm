
# Dk.Dasm

This is a simple little DCPU-16 assembler with a few nice features:

* Local labels: start a label with `.` to have it automatically prefixed with the last non-local label.
* Character literaals.
* Negative (2's complement) numbers.
* Fixed labels: `:SCREEN @ 0x8000`.
* Symbol list output.

It also supports a few output formats:

* Raw little-endian binary.
* Whitespace-delimited hex.
* Hex with leading address column.
* Hex formatted as Dasm `dat` directives.
