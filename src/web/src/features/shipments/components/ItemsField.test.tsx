import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useForm, FormProvider } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { createShipmentSchema, type CreateShipmentFormValues } from "@/features/shipments/schema";
import { ItemsField } from "./ItemsField";

function Harness() {
  const methods = useForm<CreateShipmentFormValues>({
    resolver: zodResolver(createShipmentSchema),
    defaultValues: { items: [{ description: "", quantity: 1, unitValue: 0 }] },
  });
  return (
    <FormProvider {...methods}>
      <ItemsField />
    </FormProvider>
  );
}

describe("ItemsField", () => {
  it("starts with one row and adds another on 'Add Item'", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    expect(screen.getAllByLabelText(/description/i)).toHaveLength(1);

    await user.click(screen.getByRole("button", { name: /add item/i }));

    expect(screen.getAllByLabelText(/description/i)).toHaveLength(2);
  });

  it("removes a row when its remove button is clicked", async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole("button", { name: /add item/i }));
    expect(screen.getAllByLabelText(/description/i)).toHaveLength(2);

    await user.click(screen.getAllByRole("button", { name: /remove item/i })[0]);
    expect(screen.getAllByLabelText(/description/i)).toHaveLength(1);
  });
});
