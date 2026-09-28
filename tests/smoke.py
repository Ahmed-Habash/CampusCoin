"""HTTP integration checks against a running disposable development instance.

Run: python tests/smoke.py http://localhost:5081 /absolute/path/to/test.db
Uses only Python's standard library. Never point this at a real user's database.
"""
import datetime
import html
import http.cookiejar
import json
import re
import sqlite3
import sys
import urllib.error
import urllib.parse
import urllib.request
import uuid

BASE = sys.argv[1].rstrip('/')
DB = sys.argv[2]
TODAY = datetime.date.today().isoformat()
MONTH = TODAY[:7] + '-01'
checks = []


class Client:
    def __init__(self):
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path, data=None):
        payload = None if data is None else urllib.parse.urlencode(data).encode()
        try:
            response = self.opener.open(BASE + path, payload)
            return response.status, response.read().decode(), response.url
        except urllib.error.HTTPError as error:
            return error.code, error.read().decode(), error.url

    def token(self, path):
        status, body, _ = self.request(path)
        assert status == 200, (path, status)
        match = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body)
        assert match, 'Missing CSRF token: ' + path
        return html.unescape(match.group(1))

    def post(self, path, data, token_path=None):
        return self.request(path, dict(data, __RequestVerificationToken=self.token(token_path or path)))


def check(name, condition):
    assert condition, name
    checks.append(name)
    print('PASS ' + name)


def scalar(sql, params=()):
    with sqlite3.connect(DB) as db:
        return db.execute(sql, params).fetchone()[0]


a, b, anon = Client(), Client(), Client()
check('Anonymous dashboard redirects to sign-in', '/Account/Login' in anon.request('/Dashboard')[2])
check('POST without antiforgery token rejected', anon.request('/Account/Login', {'Email': 'a@b.test', 'Password': 'x'})[0] == 400)
suffix = uuid.uuid4().hex[:10]
short_result = anon.post('/Account/Register', {'FullName': 'Short Password', 'Email': 'short' + suffix + '@example.test', 'Password': 'Abcd!12', 'ConfirmPassword': 'Abcd!12'})
check('Seven-character password rejected on server', 'at least 8 characters' in short_result[1])
eight = Client()
eight_result = eight.post('/Account/Register', {'FullName': 'Eight Characters', 'Email': 'eight' + suffix + '@example.test', 'Password': 'Abcd!123', 'ConfirmPassword': 'Abcd!123'})
check('Eight-character password accepted', '/Dashboard' in eight_result[2])
for client, name in [(a, 'Alpha'), (b, 'Beta')]:
    result = client.post('/Account/Register', {'FullName': name, 'Email': name + suffix + '@example.test',
                        'Password': 'StudentTest!2026', 'ConfirmPassword': 'StudentTest!2026'})
    check(name + ' registration and sign-in', '/Dashboard' in result[2])
check('New account starts empty', 'Your story starts here.' in a.request('/Dashboard')[1])
profile = a.post('/Profile', {'FullName': 'Alpha Updated', 'AcademicYear': 'Year 3', 'Allowance': '500', 'SavingsGoal': '125'})
check('Profile updates save', 'Your profile has been updated.' in profile[1])
check('Profile values persist', scalar('SELECT FullName FROM AspNetUsers WHERE Email LIKE ?', ('Alpha%example.test',)) == 'Alpha Updated')
assistant = a.post('/Assistant/Ask', {'Message': 'How much did I spend this month?'}, '/Dashboard')
check('Assistant answers account question', ('spent' in assistant[1].lower() or 'expenses' in assistant[1].lower()) and assistant[0] == 200)
other_assistant = b.post('/Assistant/Ask', {'Message': 'How much did I spend this month?'}, '/Dashboard')
check('Assistant is available to another signed-in account', ('spent' in other_assistant[1].lower() or 'expenses' in other_assistant[1].lower()) and other_assistant[0] == 200)
food = scalar("SELECT Id FROM Categories WHERE Name='Food' AND UserId IS NULL")
recurring = a.post('/RecurringTransactions/Edit', {'Description': 'Weekly meal budget', 'Amount': '25', 'CategoryId': food, 'Frequency': 'Weekly', 'StartDate': TODAY})
check('Create recurring transaction', 'Recurring transaction saved.' in recurring[1])
recurring_id = scalar("SELECT Id FROM RecurringTransactions WHERE Description='Weekly meal budget'")
check('Due recurring entry materializes', scalar('SELECT COUNT(*) FROM Transactions WHERE Description LIKE ?', ('Weekly meal budget%',)) == 1)
check('Recurring schedule advances', scalar('SELECT IsActive FROM RecurringTransactions WHERE Id=?', (recurring_id,)) == 1)
paused = a.post('/RecurringTransactions/Toggle/' + str(recurring_id), {}, '/RecurringTransactions')
check('Recurring transaction pauses', 'paused' in paused[1])
check('Other user cannot edit recurring transaction', b.request('/RecurringTransactions/Edit/' + str(recurring_id))[0] == 404)
a.post('/RecurringTransactions/Delete/' + str(recurring_id), {}, '/RecurringTransactions')
check('Recurring transaction removes safely', scalar('SELECT COUNT(*) FROM RecurringTransactions WHERE Id=?', (recurring_id,)) == 0)
check('Invalid dashboard month rejected', a.request('/Dashboard?month=9999-12-01')[0] == 400)
check('Invalid budget month rejected', a.request('/Budgets?month=0001-01-01')[0] == 400)
check('Malformed transaction month rejected', a.request('/Transactions?month=not-a-date')[0] == 400)

result = a.post('/Categories/Edit', {'Name': 'Test Food ' + suffix, 'Type': 'Expense'})
check('Create personal category', 'Category saved.' in result[1])
category = scalar('SELECT Id FROM Categories WHERE Name=?', ('Test Food ' + suffix,))
result = a.post('/Transactions/Edit', {'Description': 'Test lunch', 'Amount': '12.34', 'CategoryId': category, 'Date': TODAY})
check('Create transaction', 'Transaction saved.' in result[1])
transaction = scalar('SELECT Id FROM Transactions WHERE CategoryId=?', (category,))
check('Exact integer-cent storage', scalar('SELECT AmountCents FROM Transactions WHERE Id=?', (transaction,)) == 1234)
check('Other user cannot read transaction editor', b.request('/Transactions/Edit/' + str(transaction))[0] == 404)
check('Other user cannot read category editor', b.request('/Categories/Edit/' + str(category))[0] == 404)
check('Other user cannot edit transaction', b.post('/Transactions/Edit', {'Id': transaction, 'Description': 'Stolen', 'Amount': '1', 'CategoryId': category, 'Date': TODAY})[0] == 404)
check('Other user cannot delete transaction', b.post('/Transactions/Delete/' + str(transaction), {}, '/Transactions')[0] == 404)
check('Private category rejected for another student', 'Choose an available category.' in b.post('/Transactions/Edit', {'Description': 'Invalid', 'Amount': '5', 'CategoryId': category, 'Date': TODAY})[1])
check('Negative amounts rejected', 'must be between' in a.post('/Transactions/Edit', {'Description': 'Invalid', 'Amount': '-5', 'CategoryId': category, 'Date': TODAY})[1])
check('Fractional cents rejected', 'at most two decimal' in a.post('/Transactions/Edit', {'Description': 'Invalid', 'Amount': '1.234', 'CategoryId': category, 'Date': TODAY})[1])
check('Future dates rejected', 'through today' in a.post('/Transactions/Edit', {'Description': 'Invalid', 'Amount': '5', 'CategoryId': category, 'Date': '2099-01-01'})[1])

result = a.post('/Budgets/Edit', {'CategoryId': category, 'Amount': '15', 'Month': MONTH})
check('Create monthly budget', 'Monthly budget saved.' in result[1])
budget = scalar('SELECT Id FROM Budgets WHERE CategoryId=?', (category,))
check('Near-limit alert shown', 'Near limit' in a.request('/Budgets?month=' + MONTH)[1])
check('Duplicate monthly budget rejected', 'already has a budget' in a.post('/Budgets/Edit', {'CategoryId': category, 'Amount': '20', 'Month': MONTH})[1])
check('Other user cannot edit budget', b.request('/Budgets/Edit/' + str(budget))[0] == 404)
check('Other user cannot delete budget', b.post('/Budgets/Delete/' + str(budget), {}, '/Budgets')[0] == 404)
check('Used category cannot change type', 'cannot change type' in a.post('/Categories/Edit', {'Id': category, 'Name': 'Test Food ' + suffix, 'Type': 'Income'})[1])
check('Used category cannot be removed', 'category is in use' in a.post('/Categories/Delete/' + str(category), {}, '/Categories')[1])
result = a.post('/Transactions/Edit', {'Id': transaction, 'Description': 'Updated lunch', 'Amount': '18.50', 'CategoryId': category, 'Date': TODAY})
check('Edit transaction', 'Transaction saved.' in result[1])
check('Edit snapshot retained', scalar('SELECT COUNT(*) FROM TransactionRevisions WHERE TransactionId=?', (transaction,)) == 1)
check('Budget recalculated after transaction edit', 'Over budget' in a.request('/Budgets?month=' + MONTH)[1])
check('Search filter works', 'Updated lunch' in a.request('/Transactions?search=Updated')[1] and 'Updated lunch' not in a.request('/Transactions?search=Unmatched')[1])
result = a.post('/Transactions/Delete/' + str(transaction), {}, '/Transactions')
check('Transaction deletion is soft', scalar('SELECT IsDeleted FROM Transactions WHERE Id=?', (transaction,)) == 1)
check('Delete snapshot retained', scalar('SELECT COUNT(*) FROM TransactionRevisions WHERE TransactionId=?', (transaction,)) == 2)
check('Deleted expense removed from totals', '$0.00' in a.request('/Budgets?month=' + MONTH)[1] and 'On track' in a.request('/Budgets?month=' + MONTH)[1])
check('Delete budget', 'Budget removed.' in a.post('/Budgets/Delete/' + str(budget), {}, '/Budgets')[1])
check('Delete unused personal category', 'Category removed.' in a.post('/Categories/Delete/' + str(category), {}, '/Categories')[1])
check('Default category cannot be edited', a.request('/Categories/Edit/1')[0] == 404)
result = a.post('/Account/Logout', {}, '/Dashboard')
check('Logout ends authenticated session', '/Account/Login' in a.request('/Dashboard')[2])
result = a.post('/Account/Login', {'Email': 'Alpha' + suffix + '@example.test', 'Password': 'StudentTest!2026', 'ReturnUrl': 'https://example.com'})
check('External return URL is not followed', '/Dashboard' in result[2])
check('Invalid login handled', 'Email or password is incorrect.' in anon.post('/Account/Login', {'Email': 'nobody@example.test', 'Password': 'Wrong!2026'})[1])
print(json.dumps({'passed': len(checks), 'checks': checks}, indent=2))

