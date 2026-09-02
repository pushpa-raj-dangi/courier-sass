"use client";

import * as React from "react";
import { format, parseISO } from "date-fns";
import { CalendarIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Calendar } from "@/components/ui/calendar";
import { Input } from "@/components/ui/input";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { cn } from "@/lib/utils";

export interface DateTimePickerProps {
  id?: string;
  value?: string; // "yyyy-MM-ddTHH:mm", matching <input type="datetime-local"> exactly
  onChange: (value: string) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  clearable?: boolean;
  "aria-label"?: string;
}

export function DateTimePicker({
  id,
  value,
  onChange,
  placeholder = "Pick date & time",
  disabled,
  className,
  clearable = false,
  ...rest
}: DateTimePickerProps) {
  const [open, setOpen] = React.useState(false);
  const selectedDate = value ? parseISO(value) : undefined;
  const timeValue = selectedDate ? format(selectedDate, "HH:mm") : "00:00";

  function commit(date: Date, time: string) {
    onChange(`${format(date, "yyyy-MM-dd")}T${time}`);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          disabled={disabled}
          className={cn("w-full justify-start text-left font-normal", !value && "text-muted-foreground", className)}
          {...rest}
        >
          <CalendarIcon className="mr-2 h-4 w-4 shrink-0" />
          {selectedDate ? `${format(selectedDate, "PPP")} ${timeValue}` : placeholder}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto space-y-2 p-3" align="start">
        <Calendar
          mode="single"
          selected={selectedDate}
          onSelect={(date) => date && commit(date, timeValue)}
          autoFocus
        />
        <Input
          type="time"
          value={timeValue}
          onChange={(e) => commit(selectedDate ?? new Date(), e.target.value)}
        />
        {clearable && value && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="w-full"
            onClick={() => {
              onChange("");
              setOpen(false);
            }}
          >
            Clear
          </Button>
        )}
      </PopoverContent>
    </Popover>
  );
}
