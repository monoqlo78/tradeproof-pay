const { expect } = require("chai");
const { ethers } = require("hardhat");
const { anyValue } = require("@nomicfoundation/hardhat-chai-matchers/withArgs");

const TRADE_ID = 20260712001n;
const AMOUNT = 1_000_000n; // 1.00 MockUSDC (6 decimals)

describe("TradeEscrow", function () {
  let owner, buyer, seller, other;
  let token, escrow;

  beforeEach(async function () {
    [owner, buyer, seller, other] = await ethers.getSigners();

    const MockUSDC = await ethers.getContractFactory("MockUSDC");
    token = await MockUSDC.deploy(owner.address);
    await token.waitForDeployment();

    const TradeEscrow = await ethers.getContractFactory("TradeEscrow");
    escrow = await TradeEscrow.deploy(owner.address);
    await escrow.waitForDeployment();

    // Give the buyer some tokens and approve the escrow.
    await token.mint(buyer.address, AMOUNT * 10n);
    await token.connect(buyer).approve(await escrow.getAddress(), AMOUNT * 10n);
  });

  async function fund() {
    return escrow.connect(buyer).fund(TRADE_ID, seller.address, await token.getAddress(), AMOUNT);
  }

  describe("fund", function () {
    it("pulls tokens, records the escrow and emits Funded", async function () {
      const escrowAddr = await escrow.getAddress();
      await expect(fund())
        .to.emit(escrow, "Funded")
        .withArgs(TRADE_ID, buyer.address, seller.address, await token.getAddress(), AMOUNT);

      expect(await token.balanceOf(escrowAddr)).to.equal(AMOUNT);

      const e = await escrow.getEscrow(TRADE_ID);
      expect(e.buyer).to.equal(buyer.address);
      expect(e.seller).to.equal(seller.address);
      expect(e.amount).to.equal(AMOUNT);
      expect(e.status).to.equal(1n); // Funded
    });

    it("rejects double funding", async function () {
      await fund();
      await expect(fund()).to.be.revertedWith("already funded");
    });

    it("rejects zero amount / zero seller / zero token", async function () {
      const t = await token.getAddress();
      await expect(escrow.connect(buyer).fund(1n, seller.address, t, 0n)).to.be.revertedWith("amount required");
      await expect(escrow.connect(buyer).fund(1n, ethers.ZeroAddress, t, AMOUNT)).to.be.revertedWith("seller required");
      await expect(
        escrow.connect(buyer).fund(1n, seller.address, ethers.ZeroAddress, AMOUNT)
      ).to.be.revertedWith("token required");
    });
  });

  describe("release", function () {
    beforeEach(fund);

    it("pays the seller when called by owner and rejects a second release", async function () {
      await expect(escrow.connect(owner).release(TRADE_ID))
        .to.emit(escrow, "Released")
        .withArgs(TRADE_ID, seller.address, AMOUNT);
      expect(await token.balanceOf(seller.address)).to.equal(AMOUNT);

      await expect(escrow.connect(owner).release(TRADE_ID)).to.be.revertedWith("not funded");
    });

    it("can also be triggered by the buyer", async function () {
      await expect(escrow.connect(buyer).release(TRADE_ID)).to.emit(escrow, "Released");
      expect(await token.balanceOf(seller.address)).to.equal(AMOUNT);
    });

    it("rejects an unauthorized caller", async function () {
      await expect(escrow.connect(other).release(TRADE_ID)).to.be.revertedWith("not authorized");
    });

    it("rejects release on an unfunded trade", async function () {
      await expect(escrow.connect(owner).release(999n)).to.be.revertedWith("not funded");
    });
  });

  describe("refund", function () {
    beforeEach(fund);

    it("returns funds to the buyer and rejects a second refund", async function () {
      const before = await token.balanceOf(buyer.address);
      await expect(escrow.connect(owner).refund(TRADE_ID))
        .to.emit(escrow, "Refunded")
        .withArgs(TRADE_ID, buyer.address, AMOUNT);
      expect(await token.balanceOf(buyer.address)).to.equal(before + AMOUNT);

      await expect(escrow.connect(owner).refund(TRADE_ID)).to.be.revertedWith("not funded");
    });

    it("cannot refund after release", async function () {
      await escrow.connect(owner).release(TRADE_ID);
      await expect(escrow.connect(owner).refund(TRADE_ID)).to.be.revertedWith("not funded");
    });

    it("cannot release after refund", async function () {
      await escrow.connect(owner).refund(TRADE_ID);
      await expect(escrow.connect(owner).release(TRADE_ID)).to.be.revertedWith("not funded");
    });
  });

  describe("anchorHashes", function () {
    it("stores hashes and emits events, owner only", async function () {
      const key = ethers.id("invoice");
      const value = ethers.id("sha256-of-invoice");
      await expect(escrow.connect(owner).anchorHashes(TRADE_ID, [key], [value]))
        .to.emit(escrow, "HashAnchored")
        .withArgs(TRADE_ID, key, value);
      expect(await escrow.anchoredHash(TRADE_ID, key)).to.equal(value);

      await expect(escrow.connect(other).anchorHashes(TRADE_ID, [key], [value])).to.be.revertedWithCustomError(
        escrow,
        "OwnableUnauthorizedAccount"
      );
      await expect(escrow.connect(owner).anchorHashes(TRADE_ID, [key], [])).to.be.revertedWith("length mismatch");
    });
  });
});
