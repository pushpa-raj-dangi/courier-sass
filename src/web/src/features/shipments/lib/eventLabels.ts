const TITLES: Record<string, string> = {
  Created: "Booked",
  PickupAssigned: "Pickup assigned",
  PickedUp: "Picked up",
  ReceivedAtOrigin: "Received at origin hub",
  Sorted: "Sorted",
  Dispatched: "Dispatched",
  ReceivedAtDestination: "Received at destination",
  OutForDelivery: "Out for delivery",
  DeliveryFailed: "Delivery attempt failed",
  Delivered: "Delivered",
  Cancelled: "Cancelled",
  NdrCreated: "NDR opened",
  RtoInitiated: "Return initiated",
  RtoInTransit: "Return in transit",
  RtoAtOrigin: "Return at origin",
  RtoDelivered: "Returned to merchant",
};

export function eventTitle(eventType: string): string {
  if (TITLES[eventType]) return TITLES[eventType];
  const words = eventType.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}
