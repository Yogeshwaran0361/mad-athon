# ReworkGuard: New features step by step (Feature request → Profit share → Payslip)

App: http://localhost:8080 (Swagger theva illa, ellam screen la thaan)
Password: ellarukkum **Demo@12345** (Ravi, Meera mattum **Demo@12345-Demo2026**)

## Example
- Customer **Acme Ltd** oru new feature kekkuraanga: "Export invoices to PDF"
- Project **Customer Portal (ID 1)**, module **Invoicing (ID 2)**
- Quote **₹50,000**, approve aagura hours **10**
- Velai seiyravanga: **Hana (ID 8, ₹275/hr)**, **Meera (ID 10, ₹300/hr)**

---

## Step 1: Feature request create (QA)
1. **qa@reworkguard.local** login
2. Left menu → **Feature requests**
3. Form fill pannunga:
   - Project ID `1`, Module ID `2`
   - Title `Export invoices to PDF`
   - Customer `Acme Ltd`, Client reference `ACME-PO-18`
   - Quoted amount (ex tax) `50000`
4. **Submit**. Status **Requested**.

## Step 2: Manager approve
1. **manager@reworkguard.local** login → **Feature requests**
2. Antha row la **Approve** → Approved hours `10`, Assignee employee IDs `8,10` → **Approve**
3. Status **Approved**. **Task** column la puthu task number varum (e.g. #28).

## Step 3: Developer velai start (automatic InProgress)
1. **hana@reworkguard.local** login → **Tasks** → antha CR task open pannunga
2. **Move task** section la **In progress** select → move
3. Feature request **automatic ah InProgress** aagidum

## Step 4: Enhancement hours log
1. Hana: same task la **Log time** → work type **Enhancement** → `6` hours → save
2. **meera@reworkguard.local** (password `Demo@12345-Demo2026`) → same task → **Enhancement** `4` hours

## Step 5: Lead hours approve
1. **lead@reworkguard.local** → **Time approvals**
2. Hana 6h, Meera 4h rendaiyum **Approve**

## Step 6: Developer deliver (automatic Completed)
1. **hana@** → antha task → **Move task** → **Completed**
2. Feature request **automatic ah Completed** aagidum

## Step 7: Finance close (profit share automatic)
1. **finance@reworkguard.local** → **Feature requests**
2. Antha row la **Close & pay out** button → amount already `50000` fill aagi irukkum (customer kammi ah kuduththa adha maathunga) → **Close & pay out**
3. Status **Closed**. Profit share **automatic ah calculate** aagi awards create aagum.

| Calculation | |
|---|---|
| Labour cost | (6×275 + 4×300) × 1.5 = **₹4,275** |
| Profit | 50,000 − 4,275 = **₹45,725** |
| Pool (20%) | **₹9,145** |
| Hana (60%) | **₹5,487** |
| Meera (40%) | **₹3,658** |

Loss vandha pool 0. Yaarukkum cut illa.

## Step 8: Manager award approve
1. **manager@** → **Profit-share awards**
2. Hana, Meera rows **Pending** → **Approve** (venumna **Reject** + reason)

## Step 9: Payslip la paakkalam
1. **finance@** → **Payroll** → antha month run → generate → review → approve → finalize → lock
2. **hana@** → **Payslips** → profit-share earning line **₹5,487** varum

---
**Note:**
- Unga palaiya request **ACME-PO-17** (task #6): Step 3 la irundhu continue pannunga (hours already approve aagiduchu, so Step 3 → Step 6 → Step 7 → Step 8).
- Manager avanga own award ah approve panna mudiyaathu.
