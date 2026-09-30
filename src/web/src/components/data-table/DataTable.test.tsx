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
    fireEvent.click(screen.getByRole("link", { name: /go to next page/i }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it("hides the pagination nav when there is only one page", () => {
    render(
      <DataTable
        columns={columns}
        data={{
          items: [{ id: "1", name: "Row One" }],
          page: 1,
          pageSize: 10,
          totalCount: 1,
          totalPages: 1,
        }}
        isLoading={false}
        emptyTitle="No rows"
        onPageChange={vi.fn()}
        rowKey={(r) => r.id}
      />
    );

    expect(screen.getByText("Page 1 of 1 (1 total)")).toBeInTheDocument();
    expect(screen.queryByRole("navigation", { name: /pagination/i })).not.toBeInTheDocument();
  });

  it("renders numbered page links with the current page marked active", () => {
    const onPageChange = vi.fn();
    render(
      <DataTable
        columns={columns}
        data={{
          items: [{ id: "1", name: "Row One" }],
          page: 3,
          pageSize: 10,
          totalCount: 100,
          totalPages: 10,
        }}
        isLoading={false}
        emptyTitle="No rows"
        onPageChange={onPageChange}
        rowKey={(r) => r.id}
      />
    );

    expect(screen.getByRole("link", { name: "1" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "10" })).toBeInTheDocument();
    const current = screen.getByRole("link", { name: "3" });
    expect(current).toHaveAttribute("aria-current", "page");
    expect(screen.getAllByText("More pages").length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole("link", { name: "4" }));
    expect(onPageChange).toHaveBeenCalledWith(4);
  });
});

describe("DataTable selection", () => {
  const data = {
    items: [{ id: "a", name: "Alpha" }, { id: "b", name: "Beta" }],
    page: 1,
    pageSize: 20,
    totalCount: 2,
    totalPages: 1,
  };
  const columns = [{ key: "name", header: "Name", render: (r: { name: string }) => r.name }];

  it("marks the row matching selectedRowId as selected and no other", () => {
    render(
      <DataTable columns={columns} data={data} isLoading={false} emptyTitle="None" onPageChange={() => {}} rowKey={(r) => r.id} selectedRowId="b" />
    );
    expect(screen.getByText("Beta").closest("tr")).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("Alpha").closest("tr")).toHaveAttribute("aria-selected", "false");
  });

  it("omits aria-selected entirely when selection is not in use", () => {
    render(
      <DataTable columns={columns} data={data} isLoading={false} emptyTitle="None" onPageChange={() => {}} rowKey={(r) => r.id} />
    );
    expect(screen.getByText("Alpha").closest("tr")).not.toHaveAttribute("aria-selected");
  });
});
