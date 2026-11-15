package io.github.richasy.rodel.ui;

public enum MenuDensity {
    NORMAL(15, 9, 7, 24, 8, 8, 32, 48),
    COMPACT(10, 6, 5, 16, 6, 6, 20, 32);

    private final int heading;
    private final int body;
    private final int detail;
    private final int control;
    private final int inset;
    private final int gap;
    private final int icon;
    private final int row;

    MenuDensity(int heading, int body, int detail, int control, int inset, int gap, int icon, int row) {
        this.heading = heading;
        this.body = body;
        this.detail = detail;
        this.control = control;
        this.inset = inset;
        this.gap = gap;
        this.icon = icon;
        this.row = row;
    }

    public int heading() { return heading; }
    public int body() { return body; }
    public int detail() { return detail; }
    public int control() { return control; }
    public int inset() { return inset; }
    public int gap() { return gap; }
    public int icon() { return icon; }
    public int row() { return row; }
    public boolean tight() { return this == COMPACT; }
}
