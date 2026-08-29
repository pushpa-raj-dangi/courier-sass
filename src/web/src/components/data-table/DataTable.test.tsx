import { describe, expect, it, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { DataTable, type ColumnDef } from "./DataTable";

interface Row {
  id: string;
  name: string;
}

const columns: ColumnDef<Row>[] = [{ key: "name", header: "Name", render: (row) => row.name }];

describe("DataTable", () => {
  it("shows the empty state when there are no items", () => {
    render(
      <DataTable
        columns={columns}
        data={{ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }}
        isLoading={false}
        emptyTitle="No rows"
        onPageChange={vi.fn()}
        rowKey={(r) => r.id}
      />
    );
    expect(screen.getByText("No rows")).toBeInTheDocument();
  });

  it("renders rows and calls onPageChange from Next", () => {
    const onPageChange = vi.fn();
    render(
      <DataTable
        columns={columns}
        data={{
          items: [{ id: "1", name: "Row One" }],
          page: 1,
          pageSize: 10,
          totalCount: 20,
          totalPages: 2,
        }}
        isLoading={false}
        emptyTitle="No rows"
        onPageChange={onPageChange}
        rowKey={(r) => r.id}
      />
    );

    expect(screen.getByText("Row One")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /next/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });
});
