import type { Shipment } from "@/features/shipments/types";

const npr = new Intl.NumberFormat("en-NP", { style: "currency", currency: "NPR" });

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-foreground">{value}</span>
    </div>
  );
}

export function PartiesTab({ shipment }: { shipment: Shipment }) {
  const dims = shipment.lengthCm && shipment.widthCm && shipment.heightCm ? `${shipment.lengthCm} × ${shipment.widthCm} × ${shipment.heightCm} cm` : "—";
  return (
    <div className="space-y-6">
      <div className="grid grid-cols-2 gap-4">
        <Field label="Recipient" value={`${shipment.recipientName} (${shipment.recipientPhone})`} />
        <Field label="Recipient address" value={shipment.recipientAddressLine} />
        <Field label="Sender" value={`${shipment.senderName} (${shipment.senderPhone})`} />
        <Field label="Sender address" value={shipment.senderAddressLine} />
        <Field label="Weight" value={`${shipment.weightKg} kg`} />
        <Field label="Dimensions" value={dims} />
      </div>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
        <Field label="Declared value" value={npr.format(shipment.declaredValue)} />
        <Field label="COD amount" value={npr.format(shipment.codAmount)} />
        <Field label="Delivery charge" value={npr.format(shipment.deliveryCharge)} />
        <Field label="Insurance charge" value={npr.format(shipment.insuranceCharge)} />
        <Field label="Other charges" value={npr.format(shipment.otherCharges)} />
        <Field label="Total charge" value={npr.format(shipment.totalCharge)} />
      </div>
      <div className="space-y-2">
        <span className="text-xs text-muted-foreground">Items</span>
        <ul className="space-y-1">
          {shipment.items.map((item) => (
            <li key={item.id} className="flex justify-between text-sm">
              <span>{item.description} × {item.quantity}</span>
              <span>{npr.format(item.unitValue)}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
